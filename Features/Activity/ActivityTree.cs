using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

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

public sealed record CardChain(WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card)
{
    public ActivityPlace Place =>
        new(WorkspaceId: Workspace.Id, BoardId: Board.Id, ListId: List.Id, CardId: Card.Id);
}

public sealed record ChecklistChain(
    WorkspaceRef Workspace, BoardRef Board, ListRef List, CardRef Card, ChecklistRef Checklist)
{
    public ActivityPlace Place =>
        new(WorkspaceId: Workspace.Id, BoardId: Board.Id, ListId: List.Id, CardId: Card.Id);
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

    public async Task<CardChain> CardAsync(Guid cardId) =>
        await _context.Cards
            .Where(card => card.Id == cardId)
            .Join(
                _context.Lists,
                card => card.ListId,
                list => list.Id,
                (card, list) => new { card, list })
            .Join(
                _context.Boards,
                pair => pair.list.BoardId,
                board => board.Id,
                (pair, board) => new { pair.card, pair.list, board })
            .Join(
                _context.WorkSpaces,
                found => found.board.WorkspaceId,
                workspace => workspace.Id,
                (found, workspace) => new CardChain(
                    new WorkspaceRef(workspace.Id, workspace.Name),
                    new BoardRef(found.board.Id, found.board.Name),
                    new ListRef(found.list.Id, found.list.Name, found.list.Color),
                    new CardRef(found.card.Id, found.card.Title)))
            .FirstAsync();

    public async Task<ChecklistChain> ChecklistAsync(Guid checklistId) =>
        await _context.Checklists
            .Where(checklist => checklist.Id == checklistId)
            .Join(
                _context.Cards,
                checklist => checklist.CardId,
                card => card.Id,
                (checklist, card) => new { checklist, card })
            .Join(
                _context.Lists,
                pair => pair.card.ListId,
                list => list.Id,
                (pair, list) => new { pair.checklist, pair.card, list })
            .Join(
                _context.Boards,
                found => found.list.BoardId,
                board => board.Id,
                (found, board) => new { found.checklist, found.card, found.list, board })
            .Join(
                _context.WorkSpaces,
                found => found.board.WorkspaceId,
                workspace => workspace.Id,
                (found, workspace) => new ChecklistChain(
                    new WorkspaceRef(workspace.Id, workspace.Name),
                    new BoardRef(found.board.Id, found.board.Name),
                    new ListRef(found.list.Id, found.list.Name, found.list.Color),
                    new CardRef(found.card.Id, found.card.Title),
                    new ChecklistRef(found.checklist.Id, found.checklist.Name)))
            .FirstAsync();

    public async Task<AttachmentRef> AttachmentRefAsync(Guid attachmentId) =>
        await _context.Attachments
            .Where(attachment => attachment.Id == attachmentId)
            .Select(attachment => new AttachmentRef(attachment.Id, attachment.Name, attachment.Kind))
            .FirstAsync();

    public async Task<ChecklistRef> ChecklistRefAsync(Guid checklistId) =>
        await _context.Checklists
            .Where(checklist => checklist.Id == checklistId)
            .Select(checklist => new ChecklistRef(checklist.Id, checklist.Name))
            .FirstAsync();
}
