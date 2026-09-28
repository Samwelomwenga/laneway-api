using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IListCopyService
{
    Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyListDto copyListDto);
}

public sealed class ListCopyService : IListCopyService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;
    private readonly ArchiveGuard _archive;
    private readonly CopyJobSignal _signal;

    public ListCopyService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        CopyJobSignal signal)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _signal = signal;
    }

    public async Task<ApiResponse<CopyJobDto>> CopyAsync(Guid id, CopyListDto copyListDto)
    {
        ArgumentNullException.ThrowIfNull(copyListDto);

        var boardId = copyListDto.BoardId!.Value;

        if (!await _context.Lists.AnyAsync(list => list.Id == id))
        {
            return ApiResponse<CopyJobDto>.ErrorResponse("List not found", 404);
        }

        if (await RefusesAsync(id, boardId, Placement.Of(copyListDto), CopyKeep.Of(copyListDto.Keep)) is { } refused)
        {
            return refused;
        }

        var job = new CopyJob
        {
            Id = Guid.NewGuid(),
            Kind = CopyJobKind.List,
            SourceId = id,
            Request = ListCopyRequest.Of(copyListDto),
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
        var errors = new List<ApiError>();
        if (!await _context.Boards.AnyAsync(board => board.Id == boardId))
        {
            errors.Add(new ApiError("boardId", ErrorCodes.NotFound, $"Board {boardId} does not exist."));
        }
        else
        {
            errors.AddRange((await _placements.CheckOnBoardAsync(boardId, placement)).Errors);
        }

        if (errors.Count > 0)
        {
            return ReferenceErrors.Invalid<CopyJobDto>(errors);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<CopyJobDto>(archived, "boardId", TreeItem.List);
        }

        return await CountedOverCapAsync(id, keep) is { } capped
            ? ApiResponse<CopyJobDto>.ErrorResponse("This copy is too big", 409, capped)
            : null;
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
