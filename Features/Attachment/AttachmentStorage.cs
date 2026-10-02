using System.Net;
using Microsoft.Extensions.Options;
using Supabase.Storage;
using Supabase.Storage.Exceptions;
using Supabase.Storage.Interfaces;

namespace Laneway.Api;

public sealed class AttachmentStorageOptions
{
    public const string Section = "Supabase";

    public string? Url { get; set; }
    public string? ServiceRoleKey { get; set; }
    public string BucketName { get; set; } = "attachments";
}

public sealed class AttachmentStorage
{
    private readonly IStorageFileApi<FileObject> _bucket;

    public AttachmentStorage(IOptions<AttachmentStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(settings.ServiceRoleKey))
        {
            throw new InvalidOperationException(
                $"Attachment storage needs {AttachmentStorageOptions.Section}:Url and " +
                $"{AttachmentStorageOptions.Section}:ServiceRoleKey. Set both with dotnet user-secrets.");
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["apikey"] = settings.ServiceRoleKey,
            ["Authorization"] = $"Bearer {settings.ServiceRoleKey}"
        };
        _bucket = new Client($"{settings.Url.TrimEnd('/')}/storage/v1", headers).From(settings.BucketName);
    }

    public static string KeyFor(Guid cardId, Guid attachmentId) => $"cards/{cardId}/{attachmentId}";

    public Task UploadAsync(string key, byte[] bytes, string mimeType, CancellationToken cancellationToken) =>
        _bucket.Upload(
            bytes, key, new Supabase.Storage.FileOptions { ContentType = mimeType }, null, false, cancellationToken);

    public async Task<bool> TryCopyAsync(string fromKey, string toKey)
    {
        try
        {
            if (!await _bucket.Copy(fromKey, toKey))
            {
                throw new InvalidOperationException($"Storage would not copy {fromKey} to {toKey}.");
            }
        }
        catch (SupabaseStorageException gone) when (gone.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return false;
        }

        return true;
    }

    public async Task<int> RemoveAsync(List<string> keys) => (await _bucket.Remove(keys))?.Count ?? 0;

    public Task<string> SignedUrlAsync(string key, int expiresInSeconds, string fileName) =>
        _bucket.CreateSignedUrl(key, expiresInSeconds, null, new DownloadOptions { FileName = fileName });
}
