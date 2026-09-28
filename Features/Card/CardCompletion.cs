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

    public CardTally Plus(int checkItems, int checkedItems) =>
        new(CheckItemCount + checkItems, CheckedItemCount + checkedItems);
}

public sealed class CompletionWatch
{
    private readonly Card? _card;
    private readonly CardTally _before;
    private readonly bool _wasComplete;

    internal CompletionWatch(Card? card, CardTally before)
    {
        _card = card;
        _before = before;
        _wasComplete = card is not null && before.IsComplete(card);
    }

    public CompletionChange? Gaining(CardTally tally) =>
        Change(tally.CheckItemCount, tally.CheckedItemCount);

    public CompletionChange? Losing(CardTally tally) =>
        Change(-tally.CheckItemCount, -tally.CheckedItemCount);

    public CompletionChange? Change(int gainedCheckItems = 0, int gainedCheckedItems = 0)
    {
        if (_card is null)
        {
            return null;
        }

        var complete = _before.Plus(gainedCheckItems, gainedCheckedItems).IsComplete(_card);
        return complete == _wasComplete ? null : new CompletionChange(_wasComplete, complete);
    }

    public void KeepComputedValue(int losingCheckItems)
    {
        if (_card is not null && losingCheckItems > 0 && _before.CheckItemCount <= losingCheckItems)
        {
            _card.IsDueComplete = _wasComplete;
        }
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

    public async Task<CompletionWatch> WatchAsync(Guid cardId)
    {
        await _context.LockCardsAsync(cardId);
        if (await _context.Cards.FindAsync(cardId) is not { } card)
        {
            return new CompletionWatch(null, default);
        }

        await _context.Entry(card).ReloadAsync();
        return new CompletionWatch(card, await TallyAsync(cardId));
    }

    public async Task<CompletionWatch> WatchOnChecklistAsync(Guid checklistId) =>
        await WatchAsync(await _context.Checklists
            .Where(checklist => checklist.Id == checklistId)
            .Select(checklist => checklist.CardId)
            .FirstAsync());

    public async Task<CardTally> ChecklistTallyAsync(Guid checklistId)
    {
        var tally = await _context.CheckItems
            .Where(checkItem => checkItem.ChecklistId == checklistId)
            .GroupBy(checkItem => checkItem.ChecklistId)
            .Select(group => new
            {
                CheckItemCount = group.Count(),
                CheckedItemCount = group.Count(checkItem => checkItem.IsChecked)
            })
            .FirstOrDefaultAsync();

        return tally is null ? default : new CardTally(tally.CheckItemCount, tally.CheckedItemCount);
    }
}
