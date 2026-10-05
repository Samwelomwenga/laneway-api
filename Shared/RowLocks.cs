using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public static class RowLocks
{
    public static async Task<bool> TryLockBoardAsync(this ApplicationDbContext context, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var boards = await context.Boards
            .FromSql($"SELECT * FROM \"Boards\" WHERE \"Id\" = {boardId} FOR UPDATE")
            .ToListAsync();
        return boards.Count > 0;
    }

    public static async Task LockBoardsAsync(this ApplicationDbContext context, params Guid[] boardIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boardIds);

        foreach (var boardId in boardIds.Distinct().Order())
        {
            await context.TryLockBoardAsync(boardId);
        }
    }

    public static async Task LockListsAsync(this ApplicationDbContext context, params Guid[] listIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(listIds);

        foreach (var listId in listIds.Distinct().Order())
        {
            await context.Lists
                .FromSql($"SELECT * FROM \"Lists\" WHERE \"Id\" = {listId} FOR UPDATE")
                .ToListAsync();
        }
    }
}
