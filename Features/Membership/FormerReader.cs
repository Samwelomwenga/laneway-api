using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public enum ReaderPlace
{
    Workspace,
    Board
}

public class FormerReader
{
    public required Guid PlaceId { get; set; }
    public required ReaderPlace PlaceType { get; set; }
    public required Guid UserId { get; set; }
}

public static class FormerReaders
{
    public static async Task KeepBoardReadersAsync(this ApplicationDbContext context, Board board)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(board);

        var readers = context.BoardMembers.Where(member => member.BoardId == board.Id).Select(member => member.UserId);
        if (board.Visibility != BoardVisibility.Private)
        {
            readers = readers.Union(context.WorkspaceMembers
                .Where(member => member.WorkspaceId == board.WorkspaceId)
                .Select(member => member.UserId));
        }

        Keep(context, board.Id, ReaderPlace.Board, await readers.ToListAsync());
    }

    public static async Task KeepWorkspaceReadersAsync(this ApplicationDbContext context, Guid workspaceId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var readers = await context.WorkspaceMembers
            .Where(member => member.WorkspaceId == workspaceId)
            .Select(member => member.UserId)
            .ToListAsync();

        Keep(context, workspaceId, ReaderPlace.Workspace, readers);
    }

    private static void Keep(ApplicationDbContext context, Guid placeId, ReaderPlace placeType, List<Guid> readers) =>
        context.FormerReaders.AddRange(readers.Select(userId => new FormerReader
        {
            PlaceId = placeId, PlaceType = placeType, UserId = userId
        }));
}
