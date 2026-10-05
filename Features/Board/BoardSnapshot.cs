using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed record BoardSnapshot(Board Board, List<List> Lists, List<CardSnapshot> Cards, List<Label> Labels)
{
    public int FileCount => Cards.Sum(card => card.FileCount);
}

public sealed class BoardSnapshots
{
    private readonly ApplicationDbContext _context;
    private readonly CardSnapshots _cards;

    public BoardSnapshots(ApplicationDbContext context, CardSnapshots cards)
    {
        _context = context;
        _cards = cards;
    }

    public Task<BoardSnapshot?> ReadAsync(Guid boardId, bool withCards) =>
        Snapshots.ReadAsync(_context, () => ReadBoardAsync(boardId, withCards));

    private async Task<BoardSnapshot?> ReadBoardAsync(Guid boardId, bool withCards)
    {
        var board = await _context.Boards.AsNoTracking().FirstOrDefaultAsync(found => found.Id == boardId);
        if (board is null)
        {
            return null;
        }

        var lists = await _context.Lists
            .AsNoTracking()
            .Where(list => list.BoardId == boardId && !list.IsArchived)
            .InSortOrder()
            .ToListAsync();
        var labels = await _context.Labels
            .AsNoTracking()
            .Where(label => label.BoardId == boardId)
            .OrderBy(label => label.CreatedAt)
            .ThenBy(label => label.Id)
            .ToListAsync();

        return new BoardSnapshot(board, lists, await CardsOnAsync(lists, boardId, withCards), labels);
    }

    private async Task<List<CardSnapshot>> CardsOnAsync(List<List> lists, Guid boardId, bool withCards)
    {
        if (!withCards || lists.Count == 0)
        {
            return [];
        }

        var listIds = lists.ConvertAll(list => list.Id);
        var cards = await _context.Cards
            .AsNoTracking()
            .Where(card => listIds.Contains(card.ListId) && !card.IsArchived)
            .Include(card => card.Labels)
            .InSortOrder()
            .ToListAsync();

        return await _cards.OfAsync(cards, boardId);
    }
}
