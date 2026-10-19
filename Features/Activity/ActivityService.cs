using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IActivityService
{
    Task<ApiResponse<List<ActivityEntryDto>>> GetAllAsync(ActivitySearchDto searchDto);
}

public class ActivityService : IActivityService
{
    private readonly Actor _actor;
    private readonly Readable _readable;

    public ActivityService(Actor actor, Readable readable)
    {
        _actor = actor;
        _readable = readable;
    }

    public async Task<ApiResponse<List<ActivityEntryDto>>> GetAllAsync(ActivitySearchDto searchDto)
    {
        ArgumentNullException.ThrowIfNull(searchDto);

        var query = _readable.ActivityEntries(_actor.Id);

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
            pageNumber: searchDto.PageNumber,
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
        var userId = _actor.Id;
        await AddUnknownAsync(errors, "workspaceId", AccessTarget.Workspace, searchDto.WorkspaceId, _readable.Workspaces(userId), AtWorkspace);
        await AddUnknownAsync(errors, "boardId", AccessTarget.Board, searchDto.BoardId, _readable.Boards(userId), AtBoard);
        await AddUnknownAsync(errors, "listId", AccessTarget.List, searchDto.ListId, _readable.Lists(userId), AtList);
        await AddUnknownAsync(errors, "cardId", AccessTarget.Card, searchDto.CardId, _readable.Cards(userId), AtCard);
        return errors;
    }

    private async Task AddUnknownAsync<TItem>(
        List<ApiError> errors,
        string key,
        AccessTarget target,
        Guid? id,
        IQueryable<TItem> readable,
        Func<Guid, Expression<Func<ActivityEntry, bool>>> named)
        where TItem : BaseEntity
    {
        if (id is not { } value
            || await readable.AnyAsync(item => item.Id == value)
            || await _readable.ActivityEntries(_actor.Id).AnyAsync(named(value)))
        {
            return;
        }

        _readable.HiddenField(target, value, _actor.Id);
        errors.Add(new ApiError(key, ErrorCodes.NotFound, $"{AccessTargets.Name(target)} {value} does not exist."));
    }
}
