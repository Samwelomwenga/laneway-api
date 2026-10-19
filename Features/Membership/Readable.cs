using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class Readable
{
    private readonly ApplicationDbContext _context;
    private readonly DenialLog _denials;

    public Readable(ApplicationDbContext context, DenialLog denials)
    {
        _context = context;
        _denials = denials;
    }

    public IQueryable<WorkSpace> Workspaces(Guid userId) =>
        _context.WorkSpaces.Where(workspace =>
            workspace.Visibility == WorkspaceVisibility.Public
            || _context.WorkspaceMembers.Any(member => member.WorkspaceId == workspace.Id && member.UserId == userId)
            || _context.BoardMembers.Any(member => member.UserId == userId
                && _context.Boards.Any(board => board.Id == member.BoardId && board.WorkspaceId == workspace.Id)));

    public IQueryable<Board> Boards(Guid userId) =>
        _context.Boards.Where(board =>
            board.Visibility == BoardVisibility.Public
            || _context.BoardMembers.Any(member => member.BoardId == board.Id && member.UserId == userId)
            || (board.Visibility == BoardVisibility.Workspace
                && _context.WorkspaceMembers.Any(member =>
                    member.WorkspaceId == board.WorkspaceId && member.UserId == userId)));

    public IQueryable<List> Lists(Guid userId)
    {
        var boards = Boards(userId);
        return _context.Lists.Where(list => boards.Any(board => board.Id == list.BoardId));
    }

    public IQueryable<Card> Cards(Guid userId)
    {
        var lists = Lists(userId);
        return _context.Cards.Where(card => lists.Any(list => list.Id == card.ListId));
    }

    public IQueryable<Label> Labels(Guid userId)
    {
        var boards = Boards(userId);
        return _context.Labels.Where(label => boards.Any(board => board.Id == label.BoardId));
    }

    public IQueryable<Checklist> Checklists(Guid userId)
    {
        var cards = Cards(userId);
        return _context.Checklists.Where(checklist => cards.Any(card => card.Id == checklist.CardId));
    }

    public IQueryable<CheckItem> CheckItems(Guid userId)
    {
        var checklists = Checklists(userId);
        return _context.CheckItems.Where(checkItem => checklists.Any(checklist => checklist.Id == checkItem.ChecklistId));
    }

    public IQueryable<Attachment> Attachments(Guid userId)
    {
        var cards = Cards(userId);
        return _context.Attachments.Where(attachment => cards.Any(card => card.Id == attachment.CardId));
    }

    public async Task<Found<WorkSpace, WorkspaceStanding>?> WorkspaceAsync(Guid id, Guid userId)
    {
        var found = await Workspaces(userId)
            .Where(workspace => workspace.Id == id)
            .Select(workspace => new
            {
                Workspace = workspace,
                Role = _context.WorkspaceMembers
                    .Where(member => member.WorkspaceId == workspace.Id && member.UserId == userId)
                    .Select(member => (WorkspaceRole?)member.Role)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return found is null
            ? null
            : new Found<WorkSpace, WorkspaceStanding>(found.Workspace, WorkspaceStanding.Of(found.Role));
    }

    public Task<Found<Board, BoardStanding>?> BoardAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            Boards(userId).Where(board => board.Id == id).Select(board => new OnBoard<Board> { Target = board, BoardId = board.Id }),
            userId);

    public Task<Found<List, BoardStanding>?> ListAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            Lists(userId).Where(list => list.Id == id).Select(list => new OnBoard<List> { Target = list, BoardId = list.BoardId }),
            userId);

    public Task<Found<Card, BoardStanding>?> CardAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            from card in Cards(userId)
            where card.Id == id
            join list in _context.Lists on card.ListId equals list.Id
            select new OnBoard<Card> { Target = card, BoardId = list.BoardId },
            userId);

    public Task<Found<Label, BoardStanding>?> LabelAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            Labels(userId).Where(label => label.Id == id).Select(label => new OnBoard<Label> { Target = label, BoardId = label.BoardId }),
            userId);

    public Task<Found<Checklist, BoardStanding>?> ChecklistAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            from checklist in Checklists(userId)
            where checklist.Id == id
            join card in _context.Cards on checklist.CardId equals card.Id
            join list in _context.Lists on card.ListId equals list.Id
            select new OnBoard<Checklist> { Target = checklist, BoardId = list.BoardId },
            userId);

    public Task<Found<CheckItem, BoardStanding>?> CheckItemAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            from checkItem in CheckItems(userId)
            where checkItem.Id == id
            join checklist in _context.Checklists on checkItem.ChecklistId equals checklist.Id
            join card in _context.Cards on checklist.CardId equals card.Id
            join list in _context.Lists on card.ListId equals list.Id
            select new OnBoard<CheckItem> { Target = checkItem, BoardId = list.BoardId },
            userId);

    public Task<Found<Attachment, BoardStanding>?> AttachmentAsync(Guid id, Guid userId) =>
        OnBoardAsync(
            from attachment in Attachments(userId)
            where attachment.Id == id
            join card in _context.Cards on attachment.CardId equals card.Id
            join list in _context.Lists on card.ListId equals list.Id
            select new OnBoard<Attachment> { Target = attachment, BoardId = list.BoardId },
            userId);

    public ApiResponse<T> NotFound<T>(AccessTarget target, Guid id, Guid userId)
    {
        _denials.Add(new Denial(userId, target, id, null));
        return ApiResponse<T>.ErrorResponse($"{AccessTargets.Name(target)} not found", StatusCodes.Status404NotFound);
    }

    public ApiResponse<T> Refused<T>(Refusal refusal, AccessTarget target, Guid id, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        _denials.Add(new Denial(userId, target, id, refusal.Code));
        return ApiResponse<T>.ErrorResponse(
            refusal.Message,
            StatusCodes.Status403Forbidden,
            [new ApiError(null, refusal.Code, refusal.Message)]);
    }

    public bool ReadDenies<TTarget, TStanding, T>(
        [NotNullWhen(false)] Found<TTarget, TStanding>? access,
        AccessTarget target,
        Guid id,
        Guid userId,
        [NotNullWhen(true)] out ApiResponse<T>? denied)
    {
        denied = access is null ? NotFound<T>(target, id, userId) : null;
        return denied is not null;
    }

    public bool Denies<TTarget, T>(
        [NotNullWhen(false)] Found<TTarget, BoardStanding>? access,
        BoardAction action,
        AccessTarget target,
        Guid id,
        Guid userId,
        [NotNullWhen(true)] out ApiResponse<T>? denied)
    {
        if (ReadDenies(access, target, id, userId, out denied))
        {
            return true;
        }

        denied = BoardRules.Refusal(access.Standing, action) is { } refusal
            ? Refused<T>(refusal, target, id, userId)
            : null;
        return denied is not null;
    }

    public bool Denies<T>(
        [NotNullWhen(false)] Found<WorkSpace, WorkspaceStanding>? access,
        WorkspaceAction action,
        Guid id,
        Guid userId,
        [NotNullWhen(true)] out ApiResponse<T>? denied)
    {
        if (ReadDenies(access, AccessTarget.Workspace, id, userId, out denied))
        {
            return true;
        }

        denied = WorkspaceRules.Refusal(access.Standing, action) is { } refusal
            ? Refused<T>(refusal, AccessTarget.Workspace, id, userId)
            : null;
        return denied is not null;
    }

    private async Task<Found<T, BoardStanding>?> OnBoardAsync<T>(IQueryable<OnBoard<T>> located, Guid userId)
    {
        var found = await located
            .Join(_context.Boards, item => item.BoardId, board => board.Id, (item, board) => new
            {
                item.Target,
                Board = board,
                BoardRole = _context.BoardMembers
                    .Where(member => member.BoardId == board.Id && member.UserId == userId)
                    .Select(member => (BoardRole?)member.Role)
                    .FirstOrDefault(),
                WorkspaceRole = _context.WorkspaceMembers
                    .Where(member => member.WorkspaceId == board.WorkspaceId && member.UserId == userId)
                    .Select(member => (WorkspaceRole?)member.Role)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return found is null
            ? null
            : new Found<T, BoardStanding>(
                found.Target, BoardStanding.Of(found.Board, found.BoardRole, found.WorkspaceRole));
    }

    private sealed class OnBoard<T>
    {
        public required T Target { get; init; }
        public required Guid BoardId { get; init; }
    }
}
