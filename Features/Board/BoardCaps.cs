using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public readonly record struct BoardGrowth(int Cards = 0, int Lists = 0, int Labels = 0);

public sealed class BoardCaps
{
    private readonly ApplicationDbContext _context;

    public BoardCaps(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Locks the board row, then counts. A write that also locks a list or card must lock this board first,
    /// since locks go board, then list, then card. See ADR 0006.
    /// </summary>
    public async Task<List<ApiError>?> ExceededAsync(Guid boardId, BoardGrowth growth)
    {
        await _context.LockBoardsAsync(boardId);

        var has = new BoardGrowth(
            growth.Cards > 0 ? await OpenCards(_context, boardId).CountAsync() : 0,
            growth.Lists > 0 ? await _context.Lists.CountAsync(list => list.BoardId == boardId && !list.IsArchived) : 0,
            growth.Labels > 0 ? await _context.Labels.CountAsync(label => label.BoardId == boardId) : 0);

        return Exceeded(has, growth);
    }

    public static List<ApiError>? OnNewBoard(BoardGrowth growth) => Exceeded(default, growth);

    public static IQueryable<Card> OpenCards(ApplicationDbContext context, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Cards.Where(card =>
            !card.IsArchived
            && context.Lists.Any(list => list.Id == card.ListId && list.BoardId == boardId && !list.IsArchived));
    }

    public static ApiResponse<T> Full<T>(List<ApiError> errors) =>
        ApiResponse<T>.ErrorResponse("The board is full", 409, errors);

    private static List<ApiError>? Exceeded(BoardGrowth has, BoardGrowth growth)
    {
        var errors = new List<ApiError>();
        Check(errors, "open cards", FieldLimits.OpenCardsPerBoard, has.Cards, growth.Cards);
        Check(errors, "open lists", FieldLimits.OpenListsPerBoard, has.Lists, growth.Lists);
        Check(errors, "labels", FieldLimits.LabelsPerBoard, has.Labels, growth.Labels);

        return errors.Count > 0 ? errors : null;
    }

    private static void Check(List<ApiError> errors, string what, int cap, int has, int adds)
    {
        if (adds > 0 && has + adds > cap)
        {
            errors.Add(new ApiError(null, ErrorCodes.LimitReached, string.Create(CultureInfo.InvariantCulture,
                $"A board holds at most {cap:N0} {what}. This one has {has:N0}, and this change adds {adds:N0}.")));
        }
    }
}
