using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public static class RowLocks
{
    public static async Task<bool> TryLockBoardAsync(this ApplicationDbContext context, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await LockAsync(context.Boards, boardId);
    }

    public static async Task<bool> TryLockUserAsync(this ApplicationDbContext context, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await LockAsync(context.Users, userId);
    }

    public static async Task<bool> TryLockWorkspaceAsync(this ApplicationDbContext context, Guid workspaceId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await LockAsync(context.WorkSpaces, workspaceId);
    }

    public static async Task LockBoardsAsync(this ApplicationDbContext context, params Guid[] boardIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boardIds);

        foreach (var boardId in boardIds.Distinct().Order())
        {
            await LockAsync(context.Boards, boardId);
        }
    }

    public static async Task LockListsAsync(this ApplicationDbContext context, params Guid[] listIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(listIds);

        foreach (var listId in listIds.Distinct().Order())
        {
            await LockAsync(context.Lists, listId);
        }
    }

    public static async Task<bool> TryLockCardAsync(this ApplicationDbContext context, Guid cardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await LockAsync(context.Cards, cardId);
    }

    public static async Task LockCardsAsync(this ApplicationDbContext context, params Guid[] cardIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cardIds);

        foreach (var cardId in cardIds.Distinct().Order())
        {
            await LockAsync(context.Cards, cardId);
        }
    }

    public static async Task LockChecklistsAsync(this ApplicationDbContext context, params Guid[] checklistIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checklistIds);

        foreach (var checklistId in checklistIds.Distinct().Order())
        {
            await LockAsync(context.Checklists, checklistId);
        }
    }

    private static async Task<bool> LockAsync<T>(IQueryable<T> rows, Guid id) where T : BaseEntity =>
        await rows
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(row => row.SetProperty(r => r.CreatedAt, r => r.CreatedAt)) == 1;
}
