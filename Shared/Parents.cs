using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public static class Parents
{
    public static async Task<Guid?> WorkspaceOfBoardAsync(this ApplicationDbContext context, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await context.Boards
            .Where(board => board.Id == boardId)
            .Select(board => (Guid?)board.WorkspaceId)
            .FirstOrDefaultAsync();
    }

    public static async Task<Guid?> BoardOfListAsync(this ApplicationDbContext context, Guid listId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await context.Lists
            .Where(list => list.Id == listId)
            .Select(list => (Guid?)list.BoardId)
            .FirstOrDefaultAsync();
    }

    public static async Task<Guid?> ListOfCardAsync(this ApplicationDbContext context, Guid cardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await context.Cards
            .Where(card => card.Id == cardId)
            .Select(card => (Guid?)card.ListId)
            .FirstOrDefaultAsync();
    }
}
