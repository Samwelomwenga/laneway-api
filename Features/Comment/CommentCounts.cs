using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class CommentCounts
{
    private readonly ApplicationDbContext _context;

    public CommentCounts(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> OnCardAsync(Guid cardId) => (await OnCardsAsync([cardId])).GetValueOrDefault(cardId);

    public async Task<Dictionary<Guid, int>> OnCardsAsync(IReadOnlyCollection<Guid> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        var counts = await _context.ActivityEntries
            .Where(entry => entry.Type == ActivityType.Comment
                            && entry.CardId != null
                            && cardIds.Contains(entry.CardId.Value))
            .GroupBy(entry => entry.CardId!.Value)
            .Select(group => new { CardId = group.Key, Count = group.Count() })
            .ToListAsync();

        return counts.ToDictionary(counted => counted.CardId, counted => counted.Count);
    }
}
