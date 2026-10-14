using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class CardReads
{
    private readonly CardCompletion _completion;
    private readonly AttachmentCounts _attachments;
    private readonly CommentCounts _comments;

    public CardReads(CardCompletion completion, AttachmentCounts attachments, CommentCounts comments)
    {
        _completion = completion;
        _attachments = attachments;
        _comments = comments;
    }

    public async Task<CardDto> OfAsync(Card card) => (await OfAsync([card]))[0];

    public async Task<List<CardDto>> OfAsync(List<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var counts = await CountsAsync(cards.ConvertAll(c => c.Id));

        return cards.ConvertAll(card => CardView.Of(
            card,
            counts.Tallies.GetValueOrDefault(card.Id),
            counts.Attachments.GetValueOrDefault(card.Id),
            counts.Comments.GetValueOrDefault(card.Id)));
    }

    public async Task<List<BoardCardDto>> OnBoardAsync(IQueryable<Card> cards)
    {
        var rows = await cards
            .Select(card => new BoardCardRow(
                card.Id,
                card.Title,
                card.Description != "",
                card.DueDate,
                card.Position,
                card.ListId,
                card.IsDueComplete,
                card.StartDate,
                card.DueReminderMinutes,
                card.IsArchived,
                card.Labels.Select(label => label.Id).ToList(),
                card.CoverAttachmentId,
                card.CoverColor,
                card.CreatedAt,
                card.UpdatedAt,
                card.CreatedBy,
                card.UpdatedBy))
            .ToListAsync();
        var counts = await CountsAsync(rows.ConvertAll(row => row.Id));

        return rows.ConvertAll(row => CardView.OnBoard(
            row,
            counts.Tallies.GetValueOrDefault(row.Id),
            counts.Attachments.GetValueOrDefault(row.Id),
            counts.Comments.GetValueOrDefault(row.Id)));
    }

    private async Task<CardCounts> CountsAsync(List<Guid> cardIds) => new(
        await _completion.TallyAsync(cardIds),
        await _attachments.OnCardsAsync(cardIds),
        await _comments.OnCardsAsync(cardIds));

    private sealed record CardCounts(
        Dictionary<Guid, CardTally> Tallies,
        Dictionary<Guid, int> Attachments,
        Dictionary<Guid, int> Comments);
}
