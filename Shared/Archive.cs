using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IArchivable
{
    bool IsArchived { get; set; }
}

public enum ArchiveFilter
{
    Exclude,
    Only,
    Include
}

public enum CheckItemArchiveFilter
{
    Exclude,
    Include
}

public record ArchivedDto(bool? Value);

public sealed class ArchivedDtoValidator : AbstractValidator<ArchivedDto>
{
    public ArchivedDtoValidator()
    {
        RuleFor(x => x.Value).Required();
    }
}

public static class ArchiveFlag
{
    public static async Task<ApiResponse<bool>> SetArchivedAsync<TEntity>(
        this ApplicationDbContext context,
        TEntity item,
        Actor actor,
        ArchivedDto archivedDto,
        string resource,
        Func<bool, Task>? onChange = null)
        where TEntity : class, IArchivable, IStamped
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(archivedDto);

        var value = archivedDto.Value!.Value;
        if (item.IsArchived != value)
        {
            item.IsArchived = value;
            context.StampChange(item, actor);
            if (onChange is not null)
            {
                await onChange(value);
            }
            await context.SaveChangesAsync();
        }

        return ApiResponse<bool>.SuccessResponse(true, $"{resource} {(value ? "archived" : "restored")}", 204);
    }
}

public static class ArchiveView
{
    public static IQueryable<Board> Boards(IQueryable<Board> boards, ArchiveFilter filter) =>
        Apply(boards, filter, board => !board.IsArchived);

    public static IQueryable<List> Lists(
        IQueryable<List> lists, ArchiveFilter filter, ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Apply(lists, filter, list => !list.IsArchived
            && context.Boards.Any(board => board.Id == list.BoardId && !board.IsArchived));
    }

    public static IQueryable<Card> Cards(
        IQueryable<Card> cards, ArchiveFilter filter, ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Apply(cards, filter, card => !card.IsArchived
            && context.Lists.Any(list => list.Id == card.ListId && !list.IsArchived
                && context.Boards.Any(board => board.Id == list.BoardId && !board.IsArchived)));
    }

    public static IQueryable<Checklist> Checklists(
        IQueryable<Checklist> checklists, ArchiveFilter filter, ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Apply(checklists, filter, checklist => !checklist.IsArchived
            && context.Cards.Any(card => card.Id == checklist.CardId && !card.IsArchived
                && context.Lists.Any(list => list.Id == card.ListId && !list.IsArchived
                    && context.Boards.Any(board => board.Id == list.BoardId && !board.IsArchived))));
    }

    public static IQueryable<CheckItem> CheckItems(
        IQueryable<CheckItem> checkItems, CheckItemArchiveFilter filter, ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(checkItems);
        ArgumentNullException.ThrowIfNull(context);

        if (filter == CheckItemArchiveFilter.Include)
        {
            return checkItems;
        }

        var visible = Checklists(context.Checklists, ArchiveFilter.Exclude, context);
        return checkItems.Where(checkItem => visible.Any(checklist => checklist.Id == checkItem.ChecklistId));
    }

    private static IQueryable<TEntity> Apply<TEntity>(
        IQueryable<TEntity> items, ArchiveFilter filter, Expression<Func<TEntity, bool>> visible)
        where TEntity : IArchivable =>
        filter switch
        {
            ArchiveFilter.Exclude => items.Where(visible),
            ArchiveFilter.Only => items.Where(item => item.IsArchived),
            _ => items
        };
}

public enum TreeItem
{
    Board,
    List,
    Card,
    Checklist,
    CheckItem,
    Label,
    Attachment,
    Comment
}

public sealed class ArchiveGuard
{
    private readonly ApplicationDbContext _context;

    public ArchiveGuard(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TreeItem?> OnChecklistAsync(Guid checklistId)
    {
        var checklist = await _context.Checklists
            .Where(c => c.Id == checklistId)
            .Select(c => new { c.IsArchived, c.CardId })
            .FirstOrDefaultAsync();

        if (checklist is null)
        {
            return null;
        }

        return checklist.IsArchived ? TreeItem.Checklist : await OnCardAsync(checklist.CardId);
    }

    public async Task<TreeItem?> OnCardAsync(Guid cardId)
    {
        var card = await _context.Cards
            .Where(c => c.Id == cardId)
            .Select(c => new { c.IsArchived, c.ListId })
            .FirstOrDefaultAsync();

        if (card is null)
        {
            return null;
        }

        return card.IsArchived ? TreeItem.Card : await OnListAsync(card.ListId);
    }

    public async Task<TreeItem?> OnListAsync(Guid listId)
    {
        var list = await _context.Lists
            .Where(l => l.Id == listId)
            .Select(l => new { l.IsArchived, l.BoardId })
            .FirstOrDefaultAsync();

        if (list is null)
        {
            return null;
        }

        return list.IsArchived ? TreeItem.List : await OnBoardAsync(list.BoardId);
    }

    public async Task<TreeItem?> OnBoardAsync(Guid boardId) =>
        await _context.Boards.AnyAsync(b => b.Id == boardId && b.IsArchived) ? TreeItem.Board : null;
}

public static class ArchiveErrors
{
    public static ApiResponse<T> ReadOnly<T>(TreeItem archived, TreeItem subject) =>
        Blocked<T>(archived, null, Describe(archived, subject));

    public static ApiResponse<T> NoCreate<T>(TreeItem archived, string field, TreeItem subject) =>
        Blocked<T>(archived, field, Describe(archived, subject));

    public static ApiError NoCreateError(TreeItem archived, string field, TreeItem subject) =>
        new(field, ErrorCodes.Archived, Describe(archived, subject));

    public static ApiResponse<T> RestoreFirst<T>(TreeItem archived, TreeItem subject) =>
        Blocked<T>(archived, null, $"{Describe(archived, subject)} Restore the {Word(archived)} first.");

    public static ApiResponse<T> NotArchived<T>(TreeItem subject) =>
        ApiResponse<T>.ErrorResponse($"This {Word(subject)} isn't archived", 409,
            [new ApiError(null, ErrorCodes.NotArchived, $"Archive this {Word(subject)} before deleting it.")]);

    private static ApiResponse<T> Blocked<T>(TreeItem archived, string? field, string message) =>
        ApiResponse<T>.ErrorResponse($"The {Word(archived)} is archived", 409,
            [new ApiError(field, ErrorCodes.Archived, message)]);

    private static string Describe(TreeItem archived, TreeItem subject) =>
        archived == subject
            ? $"This {Word(subject)} is archived."
            : $"The {Word(archived)} this {Word(subject)} is on is archived.";

    private static string Word(TreeItem item) =>
        item switch
        {
            TreeItem.Board => "board",
            TreeItem.List => "list",
            TreeItem.Card => "card",
            TreeItem.Checklist => "checklist",
            TreeItem.CheckItem => "check item",
            TreeItem.Attachment => "attachment",
            TreeItem.Comment => "comment",
            _ => "label"
        };
}
