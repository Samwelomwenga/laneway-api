namespace DefaultNamespace;

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

    private static CardCoverDto? CoverOf(Card card) => card switch
    {
        { CoverAttachmentId: { } attachmentId } =>
            new CardCoverDto(attachmentId, null, AttachmentView.ContentPath(card.Id, attachmentId)),
        { CoverColor: { } color } => new CardCoverDto(null, color, null),
        _ => null
    };
}
