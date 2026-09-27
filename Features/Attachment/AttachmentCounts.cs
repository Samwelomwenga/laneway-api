using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed class AttachmentCounts
{
    private readonly ApplicationDbContext _context;

    public AttachmentCounts(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> OnCardAsync(Guid cardId) => (await OnCardsAsync([cardId])).GetValueOrDefault(cardId);

    public async Task<Dictionary<Guid, int>> OnCardsAsync(IReadOnlyCollection<Guid> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        var counts = await _context.Attachments
            .Where(attachment => cardIds.Contains(attachment.CardId))
            .GroupBy(attachment => attachment.CardId)
            .Select(group => new { CardId = group.Key, Count = group.Count() })
            .ToListAsync();

        return counts.ToDictionary(counted => counted.CardId, counted => counted.Count);
    }
}
