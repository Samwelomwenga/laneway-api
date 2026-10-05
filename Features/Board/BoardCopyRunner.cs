using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed class BoardCopyRunner : ICopyJobRunner
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly BoardSnapshots _snapshots;
    private readonly ObjectCopies _objects;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public BoardCopyRunner(
        ApplicationDbContext context,
        Actor actor,
        BoardSnapshots snapshots,
        ObjectCopies objects,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _snapshots = snapshots;
        _objects = objects;
        _activity = activity;
        _tree = tree;
    }

    public CopyJobKind Kind => CopyJobKind.Board;

    public async Task<CopyRunResult> RunAsync(CopyJob job, int attempt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(job);

        var request = CopyRequests.In<CopyBoardDto>(job.Request);
        var keep = BoardCopyKeep.Of(request.Keep);

        if (await _snapshots.ReadAsync(job.SourceId, keep.Cards) is not { } snapshot)
        {
            return CopyRunResult.Refused([SourceGone(job.SourceId)]);
        }

        if (CopyCaps.Exceeded(snapshot.Cards.Count, keep.Attachments ? snapshot.FileCount : 0) is { } capped)
        {
            return CopyRunResult.Refused(capped);
        }

        var plan = CopyPlan.Of(RootsOf(snapshot, keep), snapshot.Cards, keep.OnCards, DateTime.UtcNow);
        var pending = await _objects.QueueAsync(plan.Objects);
        var copied = await _objects.CopyAsync(plan.Objects);
        var copy = new BoardCopy(job, attempt, request, keep, snapshot, plan, copied, pending);

        CopyRunResult written;
        try
        {
            written = await WriteAsync(copy, token);
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

    private static IEnumerable<BaseEntity> RootsOf(BoardSnapshot snapshot, BoardCopyKeep keep)
    {
        var roots = new List<BaseEntity> { snapshot.Board };
        roots.AddRange(snapshot.Lists);
        if (keep.Labels)
        {
            roots.AddRange(snapshot.Labels);
        }

        return roots;
    }

    private async Task<CopyRunResult> WriteAsync(BoardCopy copy, CancellationToken token)
    {
        var (job, attempt, request, keep, snapshot, plan, copied, pending) = copy;
        var workspaceId = request.WorkspaceId!.Value;

        await using var transaction = await _context.Database.BeginTransactionAsync(token);

        if (await _context.WorkspaceOfBoardAsync(job.SourceId) is not { } sourceWorkspaceId)
        {
            return CopyRunResult.Refused([SourceGone(job.SourceId)]);
        }

        if (!await _context.WorkSpaces.AnyAsync(workspace => workspace.Id == workspaceId, token))
        {
            return CopyRunResult.Refused(
                [new ApiError("workspaceId", ErrorCodes.NotFound, $"Workspace {workspaceId} does not exist.")]);
        }

        var board = BuildBoard(snapshot, plan, request.Name, workspaceId);
        var labels = keep.Labels ? BuildLabels(snapshot, plan, board.Id) : [];
        var lists = BuildLists(snapshot, plan, board.Id);
        var cards = BuildCards(snapshot, plan, labels, copied);

        _context.Boards.Add(board);
        _context.Labels.AddRange(labels.Values);
        _context.Lists.AddRange(lists);
        foreach (var card in cards)
        {
            _context.Cards.Add(card.Card);
            _context.Checklists.AddRange(card.Checklists);
            _context.Attachments.AddRange(card.Attachments);
        }

        _objects.Keep(pending);
        await RecordAsync(snapshot, keep, board, sourceWorkspaceId);
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

        if (await SucceededAsync(job, attempt, board.Id, token) == 0)
        {
            return CopyRunResult.Lost;
        }

        await transaction.CommitAsync(token);

        return CopyRunResult.Done;
    }

    private Task<int> SucceededAsync(CopyJob job, int attempt, Guid boardId, CancellationToken token)
    {
        var finishedAt = DateTime.UtcNow;

        return _context.CopyJobs
            .Where(found => found.Id == job.Id && found.Attempts == attempt)
            .ExecuteUpdateAsync(set => set
                .SetProperty(found => found.Status, CopyJobStatus.Succeeded)
                .SetProperty(found => found.ResultId, (Guid?)boardId)
                .SetProperty(found => found.FinishedAt, (DateTime?)finishedAt), token);
    }

    private Board BuildBoard(BoardSnapshot snapshot, CopyPlan plan, string? name, Guid workspaceId) =>
        new()
        {
            Id = plan.IdFor(snapshot.Board.Id),
            Name = string.IsNullOrEmpty(name) ? snapshot.Board.Name : name,
            Description = snapshot.Board.Description,
            WorkspaceId = workspaceId,
            Visibility = snapshot.Board.Visibility,
            CreatedAt = plan.CreatedAtFor(snapshot.Board.Id),
            CreatedBy = _actor.Id
        };

    private Dictionary<Guid, Label> BuildLabels(BoardSnapshot snapshot, CopyPlan plan, Guid boardId) =>
        snapshot.Labels.ToDictionary(source => source.Id, source => new Label
        {
            Id = plan.IdFor(source.Id),
            Name = source.Name,
            BoardId = boardId,
            Color = source.Color,
            CreatedAt = plan.CreatedAtFor(source.Id),
            CreatedBy = _actor.Id
        });

    private List<List> BuildLists(BoardSnapshot snapshot, CopyPlan plan, Guid boardId) =>
        snapshot.Lists.ConvertAll(source => new List
        {
            Id = plan.IdFor(source.Id),
            Name = source.Name,
            BoardId = boardId,
            Color = source.Color,
            Position = source.Position,
            CreatedAt = plan.CreatedAtFor(source.Id),
            CreatedBy = _actor.Id
        });

    private List<CardRows> BuildCards(
        BoardSnapshot snapshot, CopyPlan plan, Dictionary<Guid, Label> labels, HashSet<string> copied)
    {
        var rows = new CardCopyRows(plan, _actor.Id, copied);

        return snapshot.Cards.ConvertAll(card => rows.Build(
            card,
            plan.IdFor(card.Card.ListId),
            card.Card.Position,
            card.Card.Title,
            card.Labels
                .Select(label => labels.GetValueOrDefault(label.Id))
                .OfType<Label>()
                .DistinctBy(label => label.Id)));
    }

    private async Task RecordAsync(
        BoardSnapshot snapshot, BoardCopyKeep keep, Board board, Guid sourceWorkspaceId)
    {
        var workspace = await _tree.WorkspaceAsync(board.WorkspaceId);
        var origin = sourceWorkspaceId == board.WorkspaceId
            ? null
            : new BoardOrigin(await _tree.WorkspaceAsync(sourceWorkspaceId));

        await _activity.AddAsync(
            ActivityType.CopyBoard,
            new ActivityPlace(
                WorkspaceId: workspace.Id,
                BoardId: board.Id,
                FromWorkspaceId: origin?.Workspace.Id),
            actor => new CopyBoardData(
                actor,
                workspace,
                BoardRef.Of(board),
                BoardRef.Of(snapshot.Board),
                origin,
                keep.Parts));
    }

    private static ApiError SourceGone(Guid boardId) =>
        new(null, ErrorCodes.NotFound, $"Board {boardId} does not exist.");

    private sealed record BoardCopy(
        CopyJob Job,
        int Attempt,
        CopyBoardDto Request,
        BoardCopyKeep Keep,
        BoardSnapshot Snapshot,
        CopyPlan Plan,
        HashSet<string> Copied,
        List<PendingObjectDelete> Pending);
}
