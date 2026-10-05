namespace DefaultNamespace;

public sealed record AttachmentUpload(string Name, string FileName, string MimeType, byte[] Bytes);

public record AttachmentDto
(
    Guid Id,
    Guid CardId,
    AttachmentKind Kind,
    string Name,
    string? Url,
    string? FileName,
    string? MimeType,
    int? Bytes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateLinkAttachmentDto
(
    string? Url,
    string? Name
)
{
    public string? Url { get; init; } = Url?.Trim();
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateAttachmentDto
(
    string? Name
)
{
    public string? Name { get; init; } = Name?.Trim();
}
