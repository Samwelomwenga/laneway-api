namespace DefaultNamespace;

public sealed class CardCopyPlan
{
    private readonly Dictionary<Guid, Guid> _ids;
    private readonly Dictionary<Guid, DateTime> _stamps;

    private CardCopyPlan(
        Guid cardId,
        string title,
        CopyKeep keep,
        Dictionary<Guid, Guid> ids,
        Dictionary<Guid, DateTime> stamps,
        List<ObjectCopy> objects)
    {
        CardId = cardId;
        Title = title;
        Keep = keep;
        _ids = ids;
        _stamps = stamps;
        Objects = objects;
    }

    public Guid CardId { get; }
    public string Title { get; }
    public CopyKeep Keep { get; }
    public IReadOnlyList<ObjectCopy> Objects { get; }

    public static CardCopyPlan From(CardSnapshot snapshot, string? title, CopyKeep keep, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sources = new List<BaseEntity> { snapshot.Card };
        if (keep.Checklists)
        {
            sources.AddRange(snapshot.Checklists);
            sources.AddRange(snapshot.CheckItems);
        }

        if (keep.Attachments)
        {
            sources.AddRange(snapshot.Attachments);
        }

        var start = ToMicroseconds(at);
        var ids = new Dictionary<Guid, Guid>();
        var stamps = new Dictionary<Guid, DateTime>();
        var step = 0;
        foreach (var source in sources.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id))
        {
            ids[source.Id] = Guid.NewGuid();
            stamps[source.Id] = start.AddTicks(TimeSpan.TicksPerMicrosecond * step);
            step++;
        }

        var cardId = ids[snapshot.Card.Id];
        var objects = keep.Attachments
            ? snapshot.Attachments
                .Where(attachment => attachment.ObjectKey is not null)
                .Select(attachment => new ObjectCopy(
                    attachment.ObjectKey!, AttachmentStorage.KeyFor(cardId, ids[attachment.Id])))
                .ToList()
            : [];

        return new CardCopyPlan(
            cardId,
            string.IsNullOrEmpty(title) ? snapshot.Card.Title : title,
            keep,
            ids,
            stamps,
            objects);
    }

    public Guid IdFor(Guid sourceId) => _ids[sourceId];

    public DateTime CreatedAtFor(Guid sourceId) => _stamps[sourceId];

    private static DateTime ToMicroseconds(DateTime at) =>
        new(at.Ticks - (at.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
}
