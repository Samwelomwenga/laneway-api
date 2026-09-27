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

    public static async Task LockCardsAsync(this ApplicationDbContext context, params Guid[] cardIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cardIds);

        foreach (var cardId in cardIds.Distinct().Order())
        {
            await context.Cards
                .FromSql($"SELECT * FROM \"Cards\" WHERE \"Id\" = {cardId} FOR UPDATE")
                .ToListAsync();
        }
    }

    public static async Task LockChecklistsAsync(this ApplicationDbContext context, params Guid[] checklistIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checklistIds);

        foreach (var checklistId in checklistIds.Distinct().Order())
        {
            await context.Checklists
                .FromSql($"SELECT * FROM \"Checklists\" WHERE \"Id\" = {checklistId} FOR UPDATE")
                .ToListAsync();
        }
    }
}
