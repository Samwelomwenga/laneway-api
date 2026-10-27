using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IBoardCopyService
{
    Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyBoardWrite write);
}

public sealed class BoardCopyService : IBoardCopyService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly CopyJobSignal _signal;
    private readonly Readable _readable;

    public BoardCopyService(ApplicationDbContext context, Actor actor, CopyJobSignal signal, Readable readable)
    {
        _context = context;
        _actor = actor;
        _signal = signal;
        _readable = readable;
    }

    public async Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyBoardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var workspaceId = write.WorkspaceId;

        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Board, id, _actor.Id, out ApiResponse<CopyJobDto>? denied))
        {
            return denied;
        }

        var target = await _readable.WorkspaceAsync(workspaceId, _actor.Id);
        if (_readable.TargetDenies(target, WorkspaceAction.CreateBoard, "workspaceId", workspaceId, _actor.Id, out denied))
        {
            return denied;
        }

        var keep = BoardCopyKeep.Of(write.Keep);
        if (await CountedOverCapAsync(id, keep) is { } capped)
        {
            return ApiResponse<CopyJobDto>.ErrorResponse("This copy is too big", 409, capped);
        }

        if (BoardCaps.OnNewBoard(await GrowthAsync(id, keep)) is { } full)
        {
            return BoardCaps.Full<CopyJobDto>(full);
        }

        var job = new CopyJob
        {
            Id = Guid.NewGuid(),
            Kind = CopyJobKind.Board,
            SourceId = id,
            Request = CopyRequests.Of(write),
            Status = CopyJobStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id,
            Traceparent = WorkerSpans.Origin()
        };

        _context.CopyJobs.Add(job);
        await _context.SaveChangesAsync();
        _signal.Poke();

        return ApiResponse<CopyJobDto>.SuccessResponse(CopyJobView.Of(job), "Board copy queued successfully", 202);
    }

    private async Task<BoardGrowth> GrowthAsync(Guid id, BoardCopyKeep keep) =>
        new(
            Cards: keep.Cards ? await BoardCaps.OpenCards(_context, id).CountAsync() : 0,
            Lists: await _context.Lists.CountAsync(list => list.BoardId == id && !list.IsArchived),
            Labels: keep.Labels ? await _context.Labels.CountAsync(label => label.BoardId == id) : 0);

    private async Task<List<ApiError>?> CountedOverCapAsync(Guid id, BoardCopyKeep keep)
    {
        if (!keep.Cards)
        {
            return null;
        }

        var copied = BoardCaps.OpenCards(_context, id);
        var cards = await copied.CountAsync();
        var files = keep.Attachments
            ? await _context.Attachments.CountAsync(attachment =>
                attachment.ObjectKey != null && copied.Any(card => card.Id == attachment.CardId))
            : 0;

        return CopyCaps.Exceeded(cards, files);
    }
}
