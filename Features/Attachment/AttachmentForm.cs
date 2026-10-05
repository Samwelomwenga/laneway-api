using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace DefaultNamespace;

public static class AttachmentForm
{
    private const string FilePart = "file";
    private const string NamePart = "name";
    private const int FieldBytes = 8 * 1024;
    private const int ChunkBytes = 64 * 1024;

    public static async Task<ApiResponse<AttachmentUpload>> ReadAsync(HttpRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Boundary(request) is not { } boundary)
        {
            return Malformed();
        }

        var errors = new List<ApiError>();
        string? name = null;
        var nameSeen = false;
        var fileSeen = false;
        (string FileName, byte[] Bytes)? file = null;

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(token) is { } section)
        {
            if (Part(section) is not { } part)
            {
                return Malformed();
            }

            if (fileSeen)
            {
                errors.Add(new ApiError(FilePart, ErrorCodes.InvalidFormat,
                    string.Equals(part.Name, FilePart, StringComparison.Ordinal)
                        ? $"'{FilePart}' can only be sent once."
                        : $"'{FilePart}' must be the last part of the form."));
                break;
            }

            if (string.Equals(part.Name, FilePart, StringComparison.Ordinal))
            {
                fileSeen = true;
                if (part.FileName is null)
                {
                    errors.Add(new ApiError(FilePart, ErrorCodes.InvalidFormat,
                        $"'{FilePart}' must be sent as a file part with a file name."));
                    break;
                }

                if (await ReadCappedAsync(section.Body, FieldLimits.AttachmentBytes, token) is not { } bytes)
                {
                    return TooLarge();
                }

                file = (BareName(part.FileName), bytes);
                continue;
            }

            if (string.Equals(part.Name, NamePart, StringComparison.Ordinal))
            {
                if (nameSeen)
                {
                    errors.Add(new ApiError(NamePart, ErrorCodes.InvalidFormat,
                        $"'{NamePart}' was sent more than once."));
                    continue;
                }

                nameSeen = true;
                if (await ReadCappedAsync(section.Body, FieldBytes, token) is not { } field)
                {
                    errors.Add(TooLongName());
                    continue;
                }

                name = Encoding.UTF8.GetString(field).Trim();
                if (name.Length > FieldLimits.AttachmentName)
                {
                    errors.Add(TooLongName());
                }

                continue;
            }

            errors.Add(new ApiError(part.Name, ErrorCodes.UnknownField,
                $"'{part.Name}' isn't a field on this request."));
        }

        return Finish(errors, name, fileSeen, file);
    }

    private static ApiResponse<AttachmentUpload> Finish(
        List<ApiError> errors, string? name, bool fileSeen, (string FileName, byte[] Bytes)? file)
    {
        if (file is not { } upload)
        {
            if (!fileSeen)
            {
                errors.Add(new ApiError(FilePart, ErrorCodes.Required, $"'{FilePart}' is required."));
            }

            return Invalid(errors);
        }

        if (upload.FileName.Length > FieldLimits.AttachmentFileName)
        {
            errors.Add(new ApiError(FilePart, ErrorCodes.TooLong,
                $"'{FilePart}' must have a file name of {FieldLimits.AttachmentFileName} characters or fewer."));
        }

        if (upload.Bytes.Length == 0)
        {
            errors.Add(new ApiError(FilePart, ErrorCodes.Required, $"'{FilePart}' is required."));
            return Invalid(errors);
        }

        if (AttachmentFileTypes.Of(upload.FileName, upload.Bytes) is not { } type)
        {
            errors.Add(new ApiError(FilePart, ErrorCodes.TypeNotAllowed,
                $"'{FilePart}' must be one of these types, and hold the bytes that go with it: "
                + $"{AttachmentFileTypes.Extensions}."));
            return Invalid(errors);
        }

        return errors.Count > 0
            ? Invalid(errors)
            : ApiResponse<AttachmentUpload>.SuccessResponse(new AttachmentUpload(
                string.IsNullOrEmpty(name) ? upload.FileName : name,
                upload.FileName, type.MimeType, upload.Bytes));
    }

    private static async Task<byte[]?> ReadCappedAsync(Stream body, int cap, CancellationToken token)
    {
        using var read = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        int count;
        while ((count = await body.ReadAsync(chunk, token)) > 0)
        {
            if (read.Length + count > cap)
            {
                return null;
            }

            read.Write(chunk, 0, count);
        }

        return read.ToArray();
    }

    private static (string Name, string? FileName)? Part(MultipartSection section) =>
        ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
        && HeaderUtilities.RemoveQuotes(disposition.Name).Value is { Length: > 0 } name
            ? (name, HeaderUtilities.RemoveQuotes(disposition.FileName).Value)
            : null;

    private static string BareName(string fileName) =>
        fileName[(fileName.LastIndexOfAny(['/', '\\', ':']) + 1)..];

    private static string? Boundary(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
        && HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is { Length: > 0 } boundary
            ? boundary
            : null;

    private static ApiError TooLongName() =>
        new(NamePart, ErrorCodes.TooLong, $"'{NamePart}' must be {FieldLimits.AttachmentName} characters or fewer.");

    private static ApiResponse<AttachmentUpload> Invalid(List<ApiError> errors) =>
        ApiResponse<AttachmentUpload>.ErrorResponse("Invalid data", 400, errors);

    private static ApiResponse<AttachmentUpload> Malformed() =>
        Invalid([new ApiError(null, ErrorCodes.InvalidFormat, "The request body isn't a valid multipart form.")]);

    private static ApiResponse<AttachmentUpload> TooLarge() =>
        ApiResponse<AttachmentUpload>.ErrorResponse("The file is too large", 413,
            [
                new ApiError(FilePart, ErrorCodes.TooLarge,
                    $"'{FilePart}' must be {FieldLimits.AttachmentBytes / (1024 * 1024)} MB or smaller.")
            ]);
}
