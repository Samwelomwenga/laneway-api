using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed record CardSnapshot(
    Card Card,
    Guid BoardId,
    List<Checklist> Checklists,
    List<Attachment> Attachments,
    bool IsDueComplete)
{
    public List<Label> Labels => Card.Labels;

    public IEnumerable<CheckItem> CheckItems => Checklists.SelectMany(checklist => checklist.CheckItems);

    public int FileCount => Attachments.Count(attachment => attachment.ObjectKey is not null);
}

public static class Snapshots
{
    public static async Task<T> ReadAsync<T>(ApplicationDbContext context, Func<Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(read);

        await using var transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await context.Database.ExecuteSqlAsync($"SET TRANSACTION READ ONLY");

        var snapshot = await read();
        await transaction.CommitAsync();

        return snapshot;
    }
}

public sealed class CardSnapshots
{
    private readonly ApplicationDbContext _context;

    public CardSnapshots(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<CardSnapshot?> ReadAsync(Guid cardId) =>
        Snapshots.ReadAsync(_context, () => ReadCardAsync(cardId));

    public async Task<List<CardSnapshot>> OfAsync(IReadOnlyList<Card> cards, Guid boardId)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var cardIds = cards.Select(card => card.Id).ToList();
        var checklists = await _context.Checklists
            .AsNoTracking()
            .Where(checklist => cardIds.Contains(checklist.CardId) && !checklist.IsArchived)
            .Include(checklist => checklist.CheckItems)
            .ToListAsync();
        var attachments = await _context.Attachments
            .AsNoTracking()
            .Where(attachment => cardIds.Contains(attachment.CardId))
            .ToListAsync();

        var byCard = checklists.GroupBy(checklist => checklist.CardId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var filesByCard = attachments.GroupBy(attachment => attachment.CardId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return cards.Select(card =>
        {
            var held = byCard.GetValueOrDefault(card.Id, []);
            var items = held.SelectMany(checklist => checklist.CheckItems).ToList();
            var tally = new CardTally(items.Count, items.Count(item => item.IsChecked));

            return new CardSnapshot(
                card, boardId, held, filesByCard.GetValueOrDefault(card.Id, []), tally.IsComplete(card));
        }).ToList();
    }

    private async Task<CardSnapshot?> ReadCardAsync(Guid cardId)
    {
        var card = await _context.Cards
            .AsNoTracking()
            .Include(found => found.Labels)
            .FirstOrDefaultAsync(found => found.Id == cardId);
        if (card is null)
        {
            return null;
        }

        var boardId = await _context.Lists
            .Where(list => list.Id == card.ListId)
            .Select(list => list.BoardId)
            .FirstAsync();

        return (await OfAsync([card], boardId))[0];
    }
}
