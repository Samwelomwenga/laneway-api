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
                ? ContentPath(attachment.Id)
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

    public static string ContentPath(Guid attachmentId) =>
        $"/api/v1/attachments/{attachmentId}/content";
}
