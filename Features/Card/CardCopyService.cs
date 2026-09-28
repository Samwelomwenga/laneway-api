using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICardCopyService
{
    Task<ApiResponse<CardDto>> CopyAsync(Guid id, CopyCardDto copyCardDto);
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

    public async Task<ApiResponse<CardDto>> CopyAsync(Guid id, CopyCardDto copyCardDto)
    {
        ArgumentNullException.ThrowIfNull(copyCardDto);

        var listId = copyCardDto.ListId!.Value;
        var placement = Placement.Of(copyCardDto);

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

        var plan = CardCopyPlan.From(snapshot, copyCardDto.Title, CopyKeep.Of(copyCardDto.Keep), DateTime.UtcNow);
        var pending = await _objects.QueueAsync(plan.Objects);
        var copied = await _objects.CopyAsync(plan.Objects);

        ApiResponse<CardDto> written;
        try
        {
            written = await WriteAsync(new CardCopy(snapshot, plan, listId, placement, copied, pending));
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
        var (snapshot, plan, listId, placement, copied, pending) = copy;

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
        var card = BuildCard(snapshot, plan, listId, placed.Position, labels);
        var checklists = BuildChecklists(snapshot, plan);
        var attachments = BuildAttachments(snapshot, plan, copied);

        _context.Cards.Add(card);
        _context.Checklists.AddRange(checklists);
        _context.Attachments.AddRange(attachments);
        _objects.Keep(pending);
        await RecordAsync(snapshot, plan, card, sourceListId, listId, swaps);
        await _context.SaveChangesAsync();

        if (CoverOf(snapshot, plan, attachments) is { } cover)
        {
            card.CoverAttachmentId = cover;
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        var items = checklists.SelectMany(checklist => checklist.CheckItems).ToList();
        var tally = new CardTally(items.Count, items.Count(item => item.IsChecked));

        return ApiResponse<CardDto>.SuccessResponse(
            CardView.Of(card, tally, attachments.Count, commentCount: 0), "Card copied successfully", 201);
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

    private Card BuildCard(
        CardSnapshot snapshot, CardCopyPlan plan, Guid listId, double position, List<Label> labels)
    {
        var source = snapshot.Card;
        var card = new Card
        {
            Id = plan.CardId,
            Title = plan.Title,
            Description = source.Description,
            DueDate = source.DueDate,
            Position = position,
            ListId = listId,
            IsDueComplete = snapshot.IsDueComplete,
            StartDate = source.StartDate,
            DueReminderMinutes = source.DueReminderMinutes,
            CoverColor = source.CoverColor,
            CreatedAt = plan.CreatedAtFor(source.Id),
            CreatedBy = _actor.Id
        };
        card.Labels.AddRange(labels);

        return card;
    }

    private List<Checklist> BuildChecklists(CardSnapshot snapshot, CardCopyPlan plan)
    {
        if (!plan.Keep.Checklists)
        {
            return [];
        }

        return snapshot.Checklists.ConvertAll(source => new Checklist
        {
            Id = plan.IdFor(source.Id),
            Name = source.Name,
            CardId = plan.CardId,
            Position = source.Position,
            CreatedAt = plan.CreatedAtFor(source.Id),
            CreatedBy = _actor.Id,
            CheckItems = source.CheckItems.ConvertAll(item => new CheckItem
            {
                Id = plan.IdFor(item.Id),
                Name = item.Name,
                ChecklistId = plan.IdFor(source.Id),
                Position = item.Position,
                IsChecked = item.IsChecked,
                CreatedAt = plan.CreatedAtFor(item.Id),
                CreatedBy = _actor.Id
            })
        });
    }

    private List<Attachment> BuildAttachments(CardSnapshot snapshot, CardCopyPlan plan, HashSet<string> copied)
    {
        if (!plan.Keep.Attachments)
        {
            return [];
        }

        var attachments = new List<Attachment>();
        foreach (var source in snapshot.Attachments)
        {
            var id = plan.IdFor(source.Id);
            var objectKey = source.ObjectKey is null ? null : AttachmentStorage.KeyFor(plan.CardId, id);
            if (objectKey is not null && !copied.Contains(objectKey))
            {
                continue;
            }

            attachments.Add(new Attachment
            {
                Id = id,
                CardId = plan.CardId,
                Kind = source.Kind,
                Name = source.Name,
                Url = source.Url,
                FileName = source.FileName,
                MimeType = source.MimeType,
                Bytes = source.Bytes,
                ObjectKey = objectKey,
                CreatedAt = plan.CreatedAtFor(source.Id),
                CreatedBy = _actor.Id
            });
        }

        return attachments;
    }

    private static Guid? CoverOf(CardSnapshot snapshot, CardCopyPlan plan, List<Attachment> attachments)
    {
        if (!plan.Keep.Attachments || snapshot.Card.CoverAttachmentId is not { } covering)
        {
            return null;
        }

        var id = plan.IdFor(covering);
        return attachments.Exists(attachment => attachment.Id == id) ? id : null;
    }

    private async Task RecordAsync(
        CardSnapshot snapshot,
        CardCopyPlan plan,
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
        CardCopyPlan Plan,
        Guid ListId,
        Placement Placement,
        HashSet<string> Copied,
        List<PendingObjectDelete> Pending);
}
