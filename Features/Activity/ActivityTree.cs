using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed record BoardChain(WorkspaceRef Workspace, BoardRef Board)
{
    public ActivityPlace Place => new(WorkspaceId: Workspace.Id, BoardId: Board.Id);

    public ActivityPlace PlaceOn(Guid listId) =>
        new(WorkspaceId: Workspace.Id, BoardId: Board.Id, ListId: listId);
}

public sealed record ListChain(WorkspaceRef Workspace, BoardRef Board, ListRef List)
{
    public ActivityPlace PlaceOn(Guid cardId) =>
        new(WorkspaceId: Workspace.Id, BoardId: Board.Id, ListId: List.Id, CardId: cardId);
}

public sealed class ActivityTree
{
    private readonly ApplicationDbContext _context;

    public ActivityTree(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<WorkspaceRef> WorkspaceAsync(Guid workspaceId) =>
        await FindWorkspaceAsync(workspaceId)
        ?? throw new InvalidOperationException($"Workspace {workspaceId} does not exist.");

    public async Task<WorkspaceRef?> FindWorkspaceAsync(Guid workspaceId) =>
        await _context.WorkSpaces
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => new WorkspaceRef(workspace.Id, workspace.Name))
            .FirstOrDefaultAsync();

    public async Task<BoardChain> BoardAsync(Guid boardId) =>
        await _context.Boards
            .Where(board => board.Id == boardId)
            .Join(
                _context.WorkSpaces,
                board => board.WorkspaceId,
                workspace => workspace.Id,
                (board, workspace) => new BoardChain(
                    new WorkspaceRef(workspace.Id, workspace.Name),
                    new BoardRef(board.Id, board.Name)))
            .FirstAsync();

    public async Task<ListChain> ListAsync(Guid listId) =>
        await _context.Lists
            .Where(list => list.Id == listId)
            .Join(
                _context.Boards,
                list => list.BoardId,
                board => board.Id,
                (list, board) => new { list, board })
            .Join(
                _context.WorkSpaces,
                pair => pair.board.WorkspaceId,
                workspace => workspace.Id,
                (pair, workspace) => new ListChain(
                    new WorkspaceRef(workspace.Id, workspace.Name),
                    new BoardRef(pair.board.Id, pair.board.Name),
                    new ListRef(pair.list.Id, pair.list.Name, pair.list.Color)))
            .FirstAsync();
}
