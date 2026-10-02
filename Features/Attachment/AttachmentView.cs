namespace Laneway.Api;

public static class AttachmentView
{
    public static AttachmentDto Of(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new AttachmentDto
        (
            attachment.Id,
            attachment.CardId,
            attachment.Kind,
            attachment.Name,
            attachment.Kind == AttachmentKind.File
                ? ContentPath(attachment.CardId, attachment.Id)
                : attachment.Url,
            attachment.FileName,
            attachment.MimeType,
            attachment.Bytes,
            attachment.CreatedAt,
            attachment.UpdatedAt,
            attachment.CreatedBy,
            attachment.UpdatedBy
        );
    }

    public static string ContentPath(Guid cardId, Guid attachmentId) =>
        $"/api/v1/cards/{cardId}/attachments/{attachmentId}/content";
}
