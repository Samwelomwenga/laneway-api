namespace DefaultNamespace;

public enum AttachmentKind
{
    File,
    Link
}

public class Attachment : BaseEntity
{
    public required Guid CardId { get; set; }
    public required AttachmentKind Kind { get; set; }
    public required string Name { get; set; }
    public string? Url { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public int? Bytes { get; set; }
    public string? ObjectKey { get; set; }
}

public class PendingObjectDelete
{
    public required string ObjectKey { get; set; }
    public DateTime NotBefore { get; set; }
}
