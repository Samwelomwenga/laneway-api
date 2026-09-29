using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IBoardCopyService
{
    Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyBoardWrite write);
}

public sealed class BoardCopyService : IBoardCopyService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly CopyJobSignal _signal;

    public BoardCopyService(ApplicationDbContext context, Actor actor, CopyJobSignal signal)
    {
        _context = context;
        _actor = actor;
        _signal = signal;
    }

    public async Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyBoardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var workspaceId = write.WorkspaceId;

        if (!await _context.Boards.AnyAsync(board => board.Id == id))
        {
            return ApiResponse<CopyJobDto>.ErrorResponse("Board not found", 404);
        }

        if (!await _context.WorkSpaces.AnyAsync(workspace => workspace.Id == workspaceId))
        {
            return ReferenceErrors.NotFound<CopyJobDto>("workspaceId", "Workspace", workspaceId);
        }

        if (await CountedOverCapAsync(id, BoardCopyKeep.Of(write.Keep)) is { } capped)
        {
            return ApiResponse<CopyJobDto>.ErrorResponse("This copy is too big", 409, capped);
        }

        var job = new CopyJob
        {
            Id = Guid.NewGuid(),
            Kind = CopyJobKind.Board,
            SourceId = id,
            Request = CopyRequests.Of(write),
            Status = CopyJobStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actor.Id
        };

        _context.CopyJobs.Add(job);
        await _context.SaveChangesAsync();
        _signal.Poke();

        return ApiResponse<CopyJobDto>.SuccessResponse(CopyJobView.Of(job), "Board copy queued successfully", 202);
    }

    private async Task<List<ApiError>?> CountedOverCapAsync(Guid id, BoardCopyKeep keep)
    {
        if (!keep.Cards)
        {
            return null;
        }

        var copied = _context.Cards.Where(card =>
            !card.IsArchived
            && _context.Lists.Any(list => list.Id == card.ListId && list.BoardId == id && !list.IsArchived));
        var cards = await copied.CountAsync();
        var files = keep.Attachments
            ? await _context.Attachments.CountAsync(attachment =>
                attachment.ObjectKey != null && copied.Any(card => card.Id == attachment.CardId))
            : 0;

        return CopyCaps.Exceeded(cards, files);
    }
}
