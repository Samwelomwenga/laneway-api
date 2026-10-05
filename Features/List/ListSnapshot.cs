using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed record ListSnapshot(List List, Guid BoardId, List<CardSnapshot> Cards)
{
    public int FileCount => Cards.Sum(card => card.FileCount);

    public List<Label> Labels =>
        Cards.SelectMany(card => card.Labels).DistinctBy(label => label.Id).ToList();
}

public sealed class ListSnapshots
{
    private readonly ApplicationDbContext _context;
    private readonly CardSnapshots _cards;

    public ListSnapshots(ApplicationDbContext context, CardSnapshots cards)
    {
        _context = context;
        _cards = cards;
    }

    public Task<ListSnapshot?> ReadAsync(Guid listId) =>
        Snapshots.ReadAsync(_context, () => ReadListAsync(listId));

    private async Task<ListSnapshot?> ReadListAsync(Guid listId)
    {
        var list = await _context.Lists.AsNoTracking().FirstOrDefaultAsync(found => found.Id == listId);
        if (list is null)
        {
            return null;
        }

        var cards = await _context.Cards
            .AsNoTracking()
            .Where(card => card.ListId == listId && !card.IsArchived)
            .Include(card => card.Labels)
            .InSortOrder()
            .ToListAsync();

        return new ListSnapshot(list, list.BoardId, await _cards.OfAsync(cards, list.BoardId));
    }
}
