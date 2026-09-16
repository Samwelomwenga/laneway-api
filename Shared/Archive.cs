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
        this ApplicationDbContext context, TEntity? item, Actor actor, ArchivedDto archivedDto, string resource)
        where TEntity : class, IArchivable, IStamped
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(archivedDto);

        if (item is null)
        {
            return ApiResponse<bool>.ErrorResponse($"{resource} not found", 404);
        }

        var value = archivedDto.Value!.Value;
        if (item.IsArchived != value)
        {
            item.IsArchived = value;
            context.StampChange(item, actor);
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

public static class ArchiveErrors
{
    public static ApiResponse<T> NotArchived<T>(string resource) =>
        ApiResponse<T>.ErrorResponse($"This {resource.ToLowerInvariant()} isn't archived", 409,
            [new ApiError(null, ErrorCodes.NotArchived,
                $"Archive this {resource.ToLowerInvariant()} before deleting it.")]);
}
