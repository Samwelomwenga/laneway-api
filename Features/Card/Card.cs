namespace DefaultNamespace;

public class Card: PlacedEntity, IArchivable
{
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public Guid ListId { get; set; }
    public bool IsDueComplete { get; set; }
    public string? Cover { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public int? DueReminderMinutes { get; set; }
    public bool IsArchived { get; set; }
    public List<Label> Labels { get; set; } = new List<Label>();
}
