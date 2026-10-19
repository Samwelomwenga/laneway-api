using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IListCopyService
{
    Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyListWrite write);
}

public sealed class ListCopyService : IListCopyService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly LabelMatching _labelMatching;
    private readonly BoardCaps _caps;
    private readonly CopyJobSignal _signal;
    private readonly Readable _readable;

    public ListCopyService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        LabelMatching labelMatching,
        BoardCaps caps,
        CopyJobSignal signal,
        Readable readable)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _labelMatching = labelMatching;
        _caps = caps;
        _signal = signal;
        _readable = readable;
    }

    public async Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyListWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var boardId = write.BoardId;

        var access = await _readable.ListAsync(id, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.List, id, _actor.Id, out ApiResponse<CopyJobDto>? denied))
        {
            return denied;
        }

        var target = await _readable.BoardAsync(boardId, _actor.Id);
        if (_readable.TargetDenies(target, BoardAction.Edit, "boardId", AccessTarget.Board, boardId, _actor.Id, out denied))
        {
            return denied;
        }

        if (await RefusesAsync(id, boardId, Placement.Of(write), CopyKeep.Of(write.Keep)) is { } refused)
        {
            return refused;
        }

        var job = new CopyJob
        {
            Id = Guid.NewGuid(),
            Kind = CopyJobKind.List,
            SourceId = id,
            Request = CopyRequests.Of(write),
            Status = CopyJobStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id
        };

        _context.CopyJobs.Add(job);
        await _context.SaveChangesAsync();
        _signal.Poke();

        return ApiResponse<CopyJobDto>.SuccessResponse(CopyJobView.Of(job), "List copy queued successfully", 202);
    }

    private async Task<ApiResponse<CopyJobDto>?> RefusesAsync(
        Guid id, Guid boardId, Placement placement, CopyKeep keep)
    {
        var errors = (await _placements.CheckOnBoardAsync(boardId, placement, _actor.Id)).Errors;
        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CopyJobDto>(errors);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CopyJobDto>(archived, "boardId", TreeItem.List);
        }

        if (await CountedOverCapAsync(id, keep) is { } capped)
        {
            return ApiResponse<CopyJobDto>.ErrorResponse("This copy is too big", 409, capped);
        }

        return await _caps.ExceededAsync(boardId, await GrowthAsync(id, boardId, keep)) is { } full
            ? BoardCaps.Full<CopyJobDto>(full)
            : null;
    }

    private async Task<BoardGrowth> GrowthAsync(Guid id, Guid boardId, CopyKeep keep)
    {
        var cards = await _context.Cards.CountAsync(card => card.ListId == id && !card.IsArchived);
        if (!keep.Labels || await _context.BoardOfListAsync(id) == boardId)
        {
            return new BoardGrowth(Cards: cards, Lists: 1);
        }

        var labels = await _context.Labels
            .Where(label => label.Cards.Any(card => card.ListId == id && !card.IsArchived))
            .ToListAsync();

        return new BoardGrowth(Cards: cards, Lists: 1, Labels: await _labelMatching.CountCreatedAsync(labels, boardId));
    }

    private async Task<List<ApiError>?> CountedOverCapAsync(Guid id, CopyKeep keep)
    {
        var cards = await _context.Cards.CountAsync(card => card.ListId == id && !card.IsArchived);
        var files = keep.Attachments
            ? await _context.Attachments.CountAsync(attachment =>
                attachment.ObjectKey != null
                && _context.Cards.Any(card =>
                    card.Id == attachment.CardId && card.ListId == id && !card.IsArchived))
            : 0;

        return CopyCaps.Exceeded(cards, files);
    }
}
