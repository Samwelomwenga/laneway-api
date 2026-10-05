namespace DefaultNamespace;

public delegate bool ByteCheck(ReadOnlySpan<byte> bytes);

public sealed record AttachmentFileType(string Extension, string MimeType)
{
    public required ByteCheck Matches { get; init; }

    public bool IsImage { get; init; }
}

public static class AttachmentFileTypes
{
    private const int TextScanBytes = 8 * 1024;

    private static readonly AttachmentFileType[] Allowed =
    [
        Image(Signature(".png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        Image(Signature(".jpg", "image/jpeg", [0xFF, 0xD8, 0xFF])),
        Image(Signature(".jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF])),
        Image(Gif(".gif", "image/gif")),
        Image(WebP(".webp", "image/webp")),
        Signature(".pdf", "application/pdf", "%PDF-"u8.ToArray()),
        Zip(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        Zip(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        Zip(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        Zip(".odt", "application/vnd.oasis.opendocument.text"),
        Zip(".ods", "application/vnd.oasis.opendocument.spreadsheet"),
        Zip(".odp", "application/vnd.oasis.opendocument.presentation"),
        Ole(".doc", "application/msword"),
        Ole(".xls", "application/vnd.ms-excel"),
        Ole(".ppt", "application/vnd.ms-powerpoint"),
        Text(".txt", "text/plain"),
        Text(".csv", "text/csv"),
        Text(".md", "text/markdown"),
        Zip(".zip", "application/zip")
    ];

    public static string Extensions => ExtensionsOf(Allowed);

    public static string ImageExtensions => ExtensionsOf(Allowed.Where(type => type.IsImage));

    public static bool IsImage(string? mimeType) =>
        Array.Exists(Allowed, type => type.IsImage && string.Equals(type.MimeType, mimeType, StringComparison.Ordinal));

    public static AttachmentFileType? Of(string fileName, ReadOnlySpan<byte> bytes)
    {
        var extension = Path.GetExtension(fileName);
        foreach (var type in Allowed)
        {
            if (extension.Equals(type.Extension, StringComparison.OrdinalIgnoreCase) && type.Matches(bytes))
            {
                return type;
            }
        }

        return null;
    }

    private static string ExtensionsOf(IEnumerable<AttachmentFileType> types) =>
        string.Join(", ", types.Select(type => type.Extension).Distinct());

    private static AttachmentFileType Image(AttachmentFileType type) => type with { IsImage = true };

    private static AttachmentFileType Signature(string extension, string mimeType, byte[] signature) =>
        new(extension, mimeType) { Matches = bytes => bytes.StartsWith(signature) };

    private static AttachmentFileType Gif(string extension, string mimeType) =>
        new(extension, mimeType)
        {
            Matches = bytes => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)
        };

    private static AttachmentFileType WebP(string extension, string mimeType) =>
        new(extension, mimeType)
        {
            Matches = bytes => bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)
        };

    private static AttachmentFileType Zip(string extension, string mimeType) =>
        new(extension, mimeType)
        {
            Matches = bytes => bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B
                && (bytes[2], bytes[3]) is (0x03, 0x04) or (0x05, 0x06) or (0x07, 0x08)
        };

    private static AttachmentFileType Ole(string extension, string mimeType) =>
        new(extension, mimeType)
        {
            Matches = bytes => bytes.StartsWith(
                new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })
        };

    private static AttachmentFileType Text(string extension, string mimeType) =>
        new(extension, mimeType)
        {
            Matches = bytes => !bytes[..Math.Min(bytes.Length, TextScanBytes)].Contains((byte)0)
        };
}
