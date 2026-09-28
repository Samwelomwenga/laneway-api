using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IActivityService
{
    Task<ApiResponse<List<ActivityEntryDto>>> GetAllAsync(ActivitySearchDto searchDto);
}

public class ActivityService : IActivityService
{
    private readonly ApplicationDbContext _context;

    public ActivityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<ActivityEntryDto>>> GetAllAsync(ActivitySearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        var query = _context.ActivityEntries.AsQueryable();

        if (searchDto.WorkspaceId is { } workspaceId)
        {
            query = query.Where(AtWorkspace(workspaceId));
        }
        if (searchDto.BoardId is { } boardId)
        {
            query = query.Where(AtBoard(boardId));
        }
        if (searchDto.ListId is { } listId)
        {
            query = query.Where(AtList(listId));
        }
        if (searchDto.CardId is { } cardId)
        {
            query = query.Where(AtCard(cardId));
        }
        if (searchDto.Types.Count > 0)
        {
            var types = searchDto.Types;
            query = query.Where(entry => types.Contains(entry.Type));
        }
        if (searchDto.Before is { } before)
        {
            query = query.Where(entry => entry.CreatedAt <= before.UtcDateTime);
        }
        if (searchDto.Since is { } since)
        {
            query = query.Where(entry => entry.CreatedAt > since.UtcDateTime);
        }

        var (entries, totalCount) = await PagedQuery.ReadAsync(
            query.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        if (totalCount == 0 && await UnknownIdsAsync(searchDto) is { Count: > 0 } unknown)
        {
            return ReferenceErrors.Invalid<List<ActivityEntryDto>>(unknown);
        }

        return PagedResponse<ActivityEntryDto>.SuccessResponse(
            entries.ConvertAll(ActivityView.Of),
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Activity retrieved successfully"
        );
    }

    private static Expression<Func<ActivityEntry, bool>> AtWorkspace(Guid id) =>
        entry => entry.WorkspaceId == id || entry.FromWorkspaceId == id;

    private static Expression<Func<ActivityEntry, bool>> AtBoard(Guid id) =>
        entry => entry.BoardId == id || entry.FromBoardId == id;

    private static Expression<Func<ActivityEntry, bool>> AtList(Guid id) =>
        entry => entry.ListId == id || entry.FromListId == id;

    private static Expression<Func<ActivityEntry, bool>> AtCard(Guid id) => entry => entry.CardId == id;

    private async Task<List<ApiError>> UnknownIdsAsync(ActivitySearchDto searchDto)
    {
        var errors = new List<ApiError>();
        await AddUnknownAsync<WorkSpace>(errors, "workspaceId", "Workspace", searchDto.WorkspaceId, AtWorkspace);
        await AddUnknownAsync<Board>(errors, "boardId", "Board", searchDto.BoardId, AtBoard);
        await AddUnknownAsync<List>(errors, "listId", "List", searchDto.ListId, AtList);
        await AddUnknownAsync<Card>(errors, "cardId", "Card", searchDto.CardId, AtCard);
        return errors;
    }

    private async Task AddUnknownAsync<TItem>(
        List<ApiError> errors,
        string key,
        string resource,
        Guid? id,
        Func<Guid, Expression<Func<ActivityEntry, bool>>> named)
        where TItem : BaseEntity
    {
        if (id is not { } value
            || await _context.Set<TItem>().AnyAsync(item => item.Id == value)
            || await _context.ActivityEntries.AnyAsync(named(value)))
        {
            return;
        }

        errors.Add(new ApiError(key, ErrorCodes.NotFound, $"{resource} {value} does not exist."));
    }
}
