namespace Laneway.Api;

public sealed class CopyPlan
{
    private readonly Dictionary<Guid, Guid> _ids;
    private readonly Dictionary<Guid, DateTime> _stamps;

    private CopyPlan(
        CopyKeep keep,
        Dictionary<Guid, Guid> ids,
        Dictionary<Guid, DateTime> stamps,
        List<ObjectCopy> objects)
    {
        Keep = keep;
        _ids = ids;
        _stamps = stamps;
        Objects = objects;
    }

    public CopyKeep Keep { get; }
    public IReadOnlyList<ObjectCopy> Objects { get; }

    public static CopyPlan Of(
        IEnumerable<BaseEntity> roots, IReadOnlyList<CardSnapshot> cards, CopyKeep keep, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var sources = new List<BaseEntity>(roots);
        foreach (var card in cards)
        {
            sources.Add(card.Card);
            if (keep.Checklists)
            {
                sources.AddRange(card.Checklists);
                sources.AddRange(card.CheckItems);
            }

            if (keep.Attachments)
            {
                sources.AddRange(card.Attachments);
            }
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

        return new CopyPlan(keep, ids, stamps, ObjectsFor(cards, keep, ids));
    }

    public Guid IdFor(Guid sourceId) => _ids[sourceId];

    public DateTime CreatedAtFor(Guid sourceId) => _stamps[sourceId];

    private static List<ObjectCopy> ObjectsFor(
        IReadOnlyList<CardSnapshot> cards, CopyKeep keep, Dictionary<Guid, Guid> ids)
    {
        if (!keep.Attachments)
        {
            return [];
        }

        var objects = new List<ObjectCopy>();
        foreach (var card in cards)
        {
            var cardId = ids[card.Card.Id];
            objects.AddRange(card.Attachments
                .Where(attachment => attachment.ObjectKey is not null)
                .Select(attachment => new ObjectCopy(
                    attachment.ObjectKey!, AttachmentStorage.KeyFor(cardId, ids[attachment.Id]))));
        }

        return objects;
    }

    private static DateTime ToMicroseconds(DateTime at) =>
        new(at.Ticks - (at.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
}
