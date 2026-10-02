namespace Laneway.Api;

public sealed record CardRows(Card Card, List<Checklist> Checklists, List<Attachment> Attachments, Guid? CoverId);

public sealed class CardCopyRows
{
    private readonly CopyPlan _plan;
    private readonly Guid _actorId;
    private readonly HashSet<string> _copied;

    public CardCopyRows(CopyPlan plan, Guid actorId, HashSet<string> copied)
    {
        _plan = plan;
        _actorId = actorId;
        _copied = copied;
    }

    public CardRows Build(
        CardSnapshot snapshot, Guid listId, double position, string title, IEnumerable<Label> labels)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var card = BuildCard(snapshot, listId, position, title, labels);
        var checklists = BuildChecklists(snapshot, card.Id);
        var attachments = BuildAttachments(snapshot, card.Id);

        return new CardRows(card, checklists, attachments, CoverOf(snapshot, attachments));
    }

    private Card BuildCard(
        CardSnapshot snapshot, Guid listId, double position, string title, IEnumerable<Label> labels)
    {
        var source = snapshot.Card;
        var card = new Card
        {
            Id = _plan.IdFor(source.Id),
            Title = title,
            Description = source.Description,
            DueDate = source.DueDate,
            Position = position,
            ListId = listId,
            IsDueComplete = snapshot.IsDueComplete,
            StartDate = source.StartDate,
            DueReminderMinutes = source.DueReminderMinutes,
            CoverColor = source.CoverColor,
            CreatedAt = _plan.CreatedAtFor(source.Id),
            CreatedBy = _actorId
        };
        card.Labels.AddRange(labels);

        return card;
    }

    private List<Checklist> BuildChecklists(CardSnapshot snapshot, Guid cardId)
    {
        if (!_plan.Keep.Checklists)
        {
            return [];
        }

        return snapshot.Checklists.ConvertAll(source => new Checklist
        {
            Id = _plan.IdFor(source.Id),
            Name = source.Name,
            CardId = cardId,
            Position = source.Position,
            CreatedAt = _plan.CreatedAtFor(source.Id),
            CreatedBy = _actorId,
            CheckItems = source.CheckItems.ConvertAll(item => new CheckItem
            {
                Id = _plan.IdFor(item.Id),
                Name = item.Name,
                ChecklistId = _plan.IdFor(source.Id),
                Position = item.Position,
                IsChecked = item.IsChecked,
                CreatedAt = _plan.CreatedAtFor(item.Id),
                CreatedBy = _actorId
            })
        });
    }

    private List<Attachment> BuildAttachments(CardSnapshot snapshot, Guid cardId)
    {
        if (!_plan.Keep.Attachments)
        {
            return [];
        }

        var attachments = new List<Attachment>();
        foreach (var source in snapshot.Attachments)
        {
            var id = _plan.IdFor(source.Id);
            var objectKey = source.ObjectKey is null ? null : AttachmentStorage.KeyFor(cardId, id);
            if (objectKey is not null && !_copied.Contains(objectKey))
            {
                continue;
            }

            attachments.Add(new Attachment
            {
                Id = id,
                CardId = cardId,
                Kind = source.Kind,
                Name = source.Name,
                Url = source.Url,
                FileName = source.FileName,
                MimeType = source.MimeType,
                Bytes = source.Bytes,
                ObjectKey = objectKey,
                CreatedAt = _plan.CreatedAtFor(source.Id),
                CreatedBy = _actorId
            });
        }

        return attachments;
    }

    private Guid? CoverOf(CardSnapshot snapshot, List<Attachment> attachments)
    {
        if (!_plan.Keep.Attachments || snapshot.Card.CoverAttachmentId is not { } covering)
        {
            return null;
        }

        var id = _plan.IdFor(covering);
        return attachments.Exists(attachment => attachment.Id == id) ? id : null;
    }
}
