namespace Laneway.Api;

public static class CardView
{
    public static CardDto Of(Card card, CardTally tally, int attachmentCount, int commentCount)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new CardDto
        (
            card.Id,
            card.Title,
            card.Description,
            card.DueDate,
            card.Position,
            card.ListId,
            tally.IsComplete(card),
            card.StartDate,
            card.DueReminderMinutes,
            card.IsArchived,
            card.Labels.Select(label => label.Id).ToList(),
            tally.CheckItemCount,
            tally.CheckedItemCount,
            attachmentCount,
            commentCount,
            CoverOf(card),
            card.CreatedAt,
            card.UpdatedAt,
            card.CreatedBy,
            card.UpdatedBy
        );
    }

    public static BoardCardDto OnBoard(CardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new BoardCardDto
        (
            card.Id,
            card.Title,
            card.Description.Length > 0,
            card.DueDate,
            card.Position,
            card.ListId,
            card.IsDueComplete,
            card.StartDate,
            card.DueReminderMinutes,
            card.IsArchived,
            card.LabelIds,
            card.CheckItemCount,
            card.CheckedItemCount,
            card.AttachmentCount,
            card.CommentCount,
            card.Cover,
            card.CreatedAt,
            card.UpdatedAt,
            card.CreatedBy,
            card.UpdatedBy
        );
    }

    private static CardCoverDto? CoverOf(Card card) => card switch
    {
        { CoverAttachmentId: { } attachmentId } =>
            new CardCoverDto(attachmentId, null, AttachmentView.ContentPath(attachmentId)),
        { CoverColor: { } color } => new CardCoverDto(null, color, null),
        _ => null
    };
}
