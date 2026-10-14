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
            CoverOf(card.CoverAttachmentId, card.CoverColor),
            card.CreatedAt,
            card.UpdatedAt,
            card.CreatedBy,
            card.UpdatedBy
        );
    }

    public static BoardCardDto OnBoard(BoardCardRow card, CardTally tally, int attachmentCount, int commentCount)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new BoardCardDto
        (
            card.Id,
            card.Title,
            card.HasDescription,
            card.DueDate,
            card.Position,
            card.ListId,
            tally.IsComplete(card.DueDate, card.IsDueComplete),
            card.StartDate,
            card.DueReminderMinutes,
            card.IsArchived,
            card.LabelIds,
            tally.CheckItemCount,
            tally.CheckedItemCount,
            attachmentCount,
            commentCount,
            CoverOf(card.CoverAttachmentId, card.CoverColor),
            card.CreatedAt,
            card.UpdatedAt,
            card.CreatedBy,
            card.UpdatedBy
        );
    }

    private static CardCoverDto? CoverOf(Guid? attachmentId, Color? color) => (attachmentId, color) switch
    {
        ({ } id, _) => new CardCoverDto(id, null, AttachmentView.ContentPath(id)),
        (null, { } shade) => new CardCoverDto(null, shade, null),
        _ => null
    };
}
