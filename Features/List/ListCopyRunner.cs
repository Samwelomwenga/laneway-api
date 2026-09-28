using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed class ListCopyRunner : ICopyJobRunner
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly LabelMatching _labelMatching;
    private readonly ListSnapshots _snapshots;
    private readonly ObjectCopies _objects;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public ListCopyRunner(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        LabelMatching labelMatching,
        ListSnapshots snapshots,
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

    public CopyJobKind Kind => CopyJobKind.List;

    public async Task<CopyRunResult> RunAsync(CopyJob job, int attempt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(job);

        var request = ListCopyRequest.In(job.Request);
        var keep = CopyKeep.Of(request.Keep);

        if (await _snapshots.ReadAsync(job.SourceId) is not { } snapshot)
        {
            return CopyRunResult.Refused([SourceGone(job.SourceId)]);
        }

        if (CopyCaps.Exceeded(snapshot.Cards.Count, keep.Attachments ? snapshot.FileCount : 0) is { } capped)
        {
            return CopyRunResult.Refused(capped);
        }

        var plan = CopyPlan.Of([snapshot.List], snapshot.Cards, keep, DateTime.UtcNow);
        var pending = await _objects.QueueAsync(plan.Objects);
        var copied = await _objects.CopyAsync(plan.Objects);

        CopyRunResult written;
        try
        {
            written = await WriteAsync(new ListCopy(job, attempt, request, snapshot, plan, copied, pending), token);
        }
        catch
        {
            await _objects.RemoveAsync(copied);
            throw;
        }

        if (written.Outcome != CopyRun.Done)
        {
            await _objects.RemoveAsync(copied);
        }

        return written;
    }

    private async Task<CopyRunResult> WriteAsync(ListCopy copy, CancellationToken token)
    {
        var (job, attempt, request, snapshot, plan, copied, pending) = copy;
        var boardId = request.BoardId!.Value;

        await using var transaction = await _context.Database.BeginTransactionAsync(token);

        if (await _context.BoardOfListAsync(job.SourceId) is not { } sourceBoardId)
        {
            return CopyRunResult.Refused([SourceGone(job.SourceId)]);
        }

        if (!await _context.Boards.AnyAsync(board => board.Id == boardId, token))
        {
            return CopyRunResult.Refused(
                [new ApiError("boardId", ErrorCodes.NotFound, $"Board {boardId} does not exist.")]);
        }

        var placed = await _placements.ResolveOnBoardAsync(boardId, Placement.Of(request));
        if (placed.Errors.Count > 0)
        {
            return CopyRunResult.Refused(placed.Errors);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return CopyRunResult.Refused([ArchiveErrors.NoCreateError(archived, "boardId", TreeItem.List)]);
        }

        var (labels, created) = await LabelsOnAsync(snapshot, plan.Keep, boardId);
        var list = BuildList(snapshot, plan, request.Name, boardId, placed.Position);
        var cards = BuildCards(snapshot, plan, list.Id, labels, copied);

        _context.Lists.Add(list);
        foreach (var card in cards)
        {
            _context.Cards.Add(card.Card);
            _context.Checklists.AddRange(card.Checklists);
            _context.Attachments.AddRange(card.Attachments);
        }

        _objects.Keep(pending);
        await RecordAsync(snapshot, plan.Keep, list, sourceBoardId, boardId, created);
        await _context.SaveChangesAsync(token);

        var covered = cards.FindAll(card => card.CoverId is not null);
        foreach (var card in covered)
        {
            card.Card.CoverAttachmentId = card.CoverId;
        }

        if (covered.Count > 0)
        {
            await _context.SaveChangesAsync(token);
        }

        if (await SucceededAsync(job, attempt, list.Id, token) == 0)
        {
            return CopyRunResult.Lost;
        }

        await transaction.CommitAsync(token);

        return CopyRunResult.Done;
    }

    private Task<int> SucceededAsync(CopyJob job, int attempt, Guid listId, CancellationToken token)
    {
        var finishedAt = DateTime.UtcNow;

        return _context.CopyJobs
            .Where(found => found.Id == job.Id && found.Attempts == attempt)
            .ExecuteUpdateAsync(set => set
                .SetProperty(found => found.Status, CopyJobStatus.Succeeded)
                .SetProperty(found => found.ResultId, (Guid?)listId)
                .SetProperty(found => found.FinishedAt, (DateTime?)finishedAt), token);
    }

    private List BuildList(ListSnapshot snapshot, CopyPlan plan, string? name, Guid boardId, double position) =>
        new()
        {
            Id = plan.IdFor(snapshot.List.Id),
            Name = string.IsNullOrEmpty(name) ? snapshot.List.Name : name,
            BoardId = boardId,
            Color = snapshot.List.Color,
            Position = position,
            CreatedAt = plan.CreatedAtFor(snapshot.List.Id),
            CreatedBy = _actor.Id
        };

    private List<CardRows> BuildCards(
        ListSnapshot snapshot,
        CopyPlan plan,
        Guid listId,
        Dictionary<Guid, Label> labels,
        HashSet<string> copied)
    {
        var rows = new CardCopyRows(plan, _actor.Id, copied);

        return snapshot.Cards.ConvertAll(card => rows.Build(
            card,
            listId,
            card.Card.Position,
            card.Card.Title,
            card.Labels
                .Select(label => labels.GetValueOrDefault(label.Id))
                .OfType<Label>()
                .DistinctBy(label => label.Id)));
    }

    private async Task<(Dictionary<Guid, Label> Labels, List<LabelRef>? Created)> LabelsOnAsync(
        ListSnapshot snapshot, CopyKeep keep, Guid boardId)
    {
        var sources = snapshot.Labels;
        if (!keep.Labels || sources.Count == 0)
        {
            return ([], null);
        }

        if (boardId == snapshot.BoardId)
        {
            var ids = sources.ConvertAll(label => label.Id);
            var live = await _context.Labels
                .Where(label => label.BoardId == boardId && ids.Contains(label.Id))
                .ToListAsync();

            return (live.ToDictionary(label => label.Id), null);
        }

        var matches = await _labelMatching.ToBoardAsync(sources, boardId);
        var created = matches.Where(match => match.Created).Select(match => LabelRef.Of(match.To)).ToList();

        return (
            matches.ToDictionary(match => match.From.Id, match => match.To),
            created.Count > 0 ? created : null);
    }

    private async Task RecordAsync(
        ListSnapshot snapshot,
        CopyKeep keep,
        List list,
        Guid sourceBoardId,
        Guid boardId,
        List<LabelRef>? created)
    {
        var to = await _tree.BoardAsync(boardId);
        var origin = sourceBoardId == boardId
            ? null
            : ListOrigin.Between(await _tree.BoardAsync(sourceBoardId), to);

        await _activity.AddAsync(
            ActivityType.CopyList,
            new ActivityPlace(
                WorkspaceId: to.Workspace.Id,
                BoardId: to.Board.Id,
                ListId: list.Id,
                FromWorkspaceId: origin?.Workspace?.Id,
                FromBoardId: origin?.Board.Id),
            actor => new CopyListData(
                actor,
                to.Workspace,
                to.Board,
                ListRef.Of(list),
                ListRef.Of(snapshot.List),
                origin,
                keep.Parts,
                created));
    }

    private static ApiError SourceGone(Guid listId) =>
        new(null, ErrorCodes.NotFound, $"List {listId} does not exist.");

    private sealed record ListCopy(
        CopyJob Job,
        int Attempt,
        CopyListDto Request,
        ListSnapshot Snapshot,
        CopyPlan Plan,
        HashSet<string> Copied,
        List<PendingObjectDelete> Pending);
}
