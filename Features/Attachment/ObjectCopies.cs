namespace DefaultNamespace;

public sealed record ObjectCopy(string FromKey, string ToKey);

public sealed class ObjectCopies
{
    private const int BatchSize = 10;

    private static readonly TimeSpan CopyGrace = TimeSpan.FromHours(1);

    private readonly ApplicationDbContext _context;
    private readonly AttachmentStorage _storage;
    private readonly ILogger<ObjectCopies> _logger;

    public ObjectCopies(ApplicationDbContext context, AttachmentStorage storage, ILogger<ObjectCopies> logger)
    {
        _context = context;
        _storage = storage;
        _logger = logger;
    }

    public async Task<List<PendingObjectDelete>> QueueAsync(IEnumerable<ObjectCopy> copies)
    {
        ArgumentNullException.ThrowIfNull(copies);

        var notBefore = DateTime.UtcNow.Add(CopyGrace);
        var pending = copies
            .Select(copy => new PendingObjectDelete { ObjectKey = copy.ToKey, NotBefore = notBefore })
            .ToList();
        if (pending.Count == 0)
        {
            return pending;
        }

        _context.PendingObjectDeletes.AddRange(pending);
        await _context.SaveChangesAsync();

        return pending;
    }

    public async Task<HashSet<string>> CopyAsync(IReadOnlyList<ObjectCopy> copies)
    {
        ArgumentNullException.ThrowIfNull(copies);

        var copied = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var start = 0; start < copies.Count; start += BatchSize)
            {
                var batch = copies.Skip(start).Take(BatchSize).ToList().ConvertAll(CopyOneAsync);
                try
                {
                    await Task.WhenAll(batch);
                }
                finally
                {
                    copied.UnionWith(batch
                        .Where(copy => copy.IsCompletedSuccessfully)
                        .Select(copy => copy.Result)
                        .OfType<string>());
                }
            }
        }
        catch
        {
            await RemoveAsync(copied);
            throw;
        }

        return copied;
    }

    public void Keep(IEnumerable<PendingObjectDelete> pending) =>
        _context.PendingObjectDeletes.RemoveRange(pending);

    public async Task RemoveAsync(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return;
        }

        try
        {
            await _storage.RemoveAsync([.. keys]);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Could not remove {Count} objects after a failed copy. The outbox will retry them.", keys.Count);
        }
    }

    private async Task<string?> CopyOneAsync(ObjectCopy copy) =>
        await _storage.TryCopyAsync(copy.FromKey, copy.ToKey) ? copy.ToKey : null;
}
