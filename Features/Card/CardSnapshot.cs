using System.Data;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public sealed record CardSnapshot(
    Card Card,
    Guid BoardId,
    List<Checklist> Checklists,
    List<Attachment> Attachments,
    bool IsDueComplete)
{
    public List<Label> Labels => Card.Labels;

    public IEnumerable<CheckItem> CheckItems => Checklists.SelectMany(checklist => checklist.CheckItems);
}

public sealed class CardSnapshots
{
    private readonly ApplicationDbContext _context;

    public CardSnapshots(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CardSnapshot?> ReadAsync(Guid cardId)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await _context.Database.ExecuteSqlAsync($"SET TRANSACTION READ ONLY");

        var snapshot = await ReadCardAsync(cardId);
        await transaction.CommitAsync();

        return snapshot;
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

        var checklists = await _context.Checklists
            .AsNoTracking()
            .Where(checklist => checklist.CardId == cardId && !checklist.IsArchived)
            .Include(checklist => checklist.CheckItems)
            .ToListAsync();

        var attachments = await _context.Attachments
            .AsNoTracking()
            .Where(attachment => attachment.CardId == cardId)
            .ToListAsync();

        var items = checklists.SelectMany(checklist => checklist.CheckItems).ToList();
        var tally = new CardTally(items.Count, items.Count(item => item.IsChecked));

        return new CardSnapshot(card, boardId, checklists, attachments, tally.IsComplete(card));
    }
}
