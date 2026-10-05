using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public readonly record struct CardTally(int CheckItemCount, int CheckedItemCount)
{
    public bool IsComplete(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.DueDate is not null
               && (CheckItemCount > 0 ? CheckedItemCount == CheckItemCount : card.IsDueComplete);
    }
}

public sealed class CardCompletion
{
    private readonly ApplicationDbContext _context;

    public CardCompletion(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CardTally> TallyAsync(Guid cardId) =>
        (await TallyAsync([cardId])).GetValueOrDefault(cardId);

    public async Task<Dictionary<Guid, CardTally>> TallyAsync(IReadOnlyCollection<Guid> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        var tallies = await _context.Checklists
            .Where(checklist => cardIds.Contains(checklist.CardId) && !checklist.IsArchived)
            .SelectMany(
                checklist => checklist.CheckItems,
                (checklist, checkItem) => new { checklist.CardId, checkItem.IsChecked })
            .GroupBy(counted => counted.CardId)
            .Select(group => new
            {
                CardId = group.Key,
                CheckItemCount = group.Count(),
                CheckedItemCount = group.Count(counted => counted.IsChecked)
            })
            .ToListAsync();

        return tallies.ToDictionary(
            tally => tally.CardId,
            tally => new CardTally(tally.CheckItemCount, tally.CheckedItemCount));
    }

    public Expression<Func<Card, bool>> Matching(bool isDueComplete) =>
        card => card.DueDate != null
                && (!_context.Checklists.Any(checklist =>
                        checklist.CardId == card.Id && !checklist.IsArchived
                        && checklist.CheckItems.Any(checkItem => !checkItem.IsChecked))
                    && (_context.Checklists.Any(checklist =>
                            checklist.CardId == card.Id && !checklist.IsArchived
                            && checklist.CheckItems.Any())
                        || card.IsDueComplete)) == isDueComplete;

    public async Task KeepComputedValueAsync(Guid cardId, int losingCheckItems)
    {
        if (losingCheckItems <= 0)
        {
            return;
        }

        var tally = await TallyAsync(cardId);
        if (tally.CheckItemCount > losingCheckItems)
        {
            return;
        }

        if (await _context.Cards.FindAsync(cardId) is { } card)
        {
            card.IsDueComplete = tally.IsComplete(card);
        }
    }
}
