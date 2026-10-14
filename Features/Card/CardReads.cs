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

        var cardIds = cards.ConvertAll(c => c.Id);
        var tallies = await _completion.TallyAsync(cardIds);
        var attachmentCounts = await _attachments.OnCardsAsync(cardIds);
        var commentCounts = await _comments.OnCardsAsync(cardIds);

        return cards.ConvertAll(card => CardView.Of(
            card,
            tallies.GetValueOrDefault(card.Id),
            attachmentCounts.GetValueOrDefault(card.Id),
            commentCounts.GetValueOrDefault(card.Id)));
    }
}
