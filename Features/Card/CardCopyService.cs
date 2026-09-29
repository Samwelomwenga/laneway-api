using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardCopyService
{
    Task<ApiResponse<CardDto>> CopyAsync(Guid id, CopyCardWrite write);
}

public sealed class CardCopyService : ICardCopyService
{
    private const int WriteAttempts = 3;

    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly LabelMatching _labelMatching;
    private readonly CardSnapshots _snapshots;
    private readonly ObjectCopies _objects;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public CardCopyService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        LabelMatching labelMatching,
        CardSnapshots snapshots,
        ObjectCopies objects,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _labelMatching = labelMatching;
        _snapshots = snapshots;
        _objects = objects;
        _activity = activity;
        _tree = tree;
    }

    public async Task<ApiResponse<CardDto>> CopyAsync(Guid id, CopyCardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var listId = write.ListId;
        var placement = Placement.Of(write);

        if (!await _context.Cards.AnyAsync(card => card.Id == id))
        {
            return CardNotFound();
        }

        if (await RefusesAsync(listId, placement) is { } refused)
        {
            return refused;
        }

        _context.ChangeTracker.Clear();

        if (await _snapshots.ReadAsync(id) is not { } snapshot)
        {
            return CardNotFound();
        }

        var plan = CopyPlan.Of([], [snapshot], CopyKeep.Of(write.Keep), DateTime.UtcNow);
        var title = string.IsNullOrEmpty(write.Title) ? snapshot.Card.Title : write.Title;
        var pending = await _objects.QueueAsync(plan.Objects);
        var copied = await _objects.CopyAsync(plan.Objects);

        ApiResponse<CardDto> written;
        try
        {
            written = await WriteAsync(new CardCopy(snapshot, plan, title, listId, placement, copied, pending));
        }
        catch
        {
            await _objects.RemoveAsync(copied);
            throw;
        }

        if (!written.Success)
        {
            await _objects.RemoveAsync(copied);
        }

        return written;
    }

    private async Task<ApiResponse<CardDto>?> RefusesAsync(Guid listId, Placement placement)
    {
        var errors = new List<ApiError>();
        if (await _context.BoardOfListAsync(listId) is null)
        {
            errors.Add(new ApiError("listId", ErrorCodes.NotFound, $"List {listId} does not exist."));
        }
        else
        {
            errors.AddRange((await _placements.CheckInListAsync(listId, placement)).Errors);
        }

        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(errors);
        }

        return await _archive.OnListAsync(listId) is { } archived
            ? ArchiveErrors.NoCreate<CardDto>(archived, "listId", TreeItem.Card)
            : null;
    }

    private async Task<ApiResponse<CardDto>> WriteAsync(CardCopy copy)
    {
        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            if (await WriteOnceAsync(copy) is { } response)
            {
                return response;
            }

            _context.ChangeTracker.Clear();
        }

        throw new InvalidOperationException(
            $"Card {copy.Snapshot.Card.Id} could not copy into list {copy.ListId}. "
            + "The list kept changing board.");
    }

    private async Task<ApiResponse<CardDto>?> WriteOnceAsync(CardCopy copy)
    {
        var (snapshot, plan, title, listId, placement, copied, pending) = copy;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (await _context.ListOfCardAsync(snapshot.Card.Id) is not { } sourceListId)
        {
            return CardNotFound();
        }

        if (await _context.BoardOfListAsync(listId) is not { } boardId)
        {
            return ReferenceErrors.NotFound<CardDto>("listId", "List", listId);
        }

        if (plan.Keep.Labels)
        {
            await _context.TryLockBoardAsync(boardId);
        }

        var placed = await _placements.ResolveInListAsync(listId, placement);

        if (await _context.BoardOfListAsync(listId) != boardId)
        {
            return null;
        }

        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CardDto>(placed.Errors);
        }

        if (await _archive.OnListAsync(listId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CardDto>(archived, "listId", TreeItem.Card);
        }

        var (labels, swaps) = await LabelsOnAsync(snapshot, plan.Keep, boardId);
        var rows = new CardCopyRows(plan, _actor.Id, copied)
            .Build(snapshot, listId, placed.Position, title, labels);

        _context.Cards.Add(rows.Card);
        _context.Checklists.AddRange(rows.Checklists);
        _context.Attachments.AddRange(rows.Attachments);
        _objects.Keep(pending);
        await RecordAsync(snapshot, plan, rows.Card, sourceListId, listId, swaps);
        await _context.SaveChangesAsync();

        if (rows.CoverId is { } cover)
        {
            rows.Card.CoverAttachmentId = cover;
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        var items = rows.Checklists.SelectMany(checklist => checklist.CheckItems).ToList();
        var tally = new CardTally(items.Count, items.Count(item => item.IsChecked));

        return ApiResponse<CardDto>.SuccessResponse(
            CardView.Of(rows.Card, tally, rows.Attachments.Count, commentCount: 0), "Card copied successfully", 201);
    }

    private async Task<(List<Label> Labels, List<LabelSwap>? Swaps)> LabelsOnAsync(
        CardSnapshot snapshot, CopyKeep keep, Guid boardId)
    {
        if (!keep.Labels || snapshot.Labels.Count == 0)
        {
            return ([], null);
        }

        if (boardId == snapshot.BoardId)
        {
            var ids = snapshot.Labels.ConvertAll(label => label.Id);
            return (
                await _context.Labels
                    .Where(label => label.BoardId == boardId && ids.Contains(label.Id))
                    .ToListAsync(),
                null);
        }

        var matches = await _labelMatching.ToBoardAsync(snapshot.Labels, boardId);
        return (
            matches.ConvertAll(match => match.To).DistinctBy(label => label.Id).ToList(),
            matches.ConvertAll(match =>
                new LabelSwap(LabelRef.Of(match.From), LabelRef.Of(match.To), match.Created)));
    }

    private async Task RecordAsync(
        CardSnapshot snapshot,
        CopyPlan plan,
        Card card,
        Guid sourceListId,
        Guid listId,
        List<LabelSwap>? swaps)
    {
        var to = await _tree.ListAsync(listId);
        var origin = sourceListId == listId
            ? null
            : CardOrigin.Between(await _tree.ListAsync(sourceListId), to);

        await _activity.AddAsync(
            ActivityType.CopyCard,
            new ActivityPlace(
                WorkspaceId: to.Workspace.Id,
                BoardId: to.Board.Id,
                ListId: to.List.Id,
                CardId: card.Id,
                FromWorkspaceId: origin?.Workspace?.Id,
                FromBoardId: origin?.Board?.Id,
                FromListId: origin?.List.Id),
            actor => new CopyCardData(
                actor,
                to.Workspace,
                to.Board,
                to.List,
                CardRef.Of(card),
                CardRef.Of(snapshot.Card),
                origin,
                plan.Keep.Parts,
                swaps));
    }

    private static ApiResponse<CardDto> CardNotFound() =>
        ApiResponse<CardDto>.ErrorResponse("Card not found", 404);

    private sealed record CardCopy(
        CardSnapshot Snapshot,
        CopyPlan Plan,
        string Title,
        Guid ListId,
        Placement Placement,
        HashSet<string> Copied,
        List<PendingObjectDelete> Pending);
}
