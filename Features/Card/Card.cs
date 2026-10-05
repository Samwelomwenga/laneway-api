namespace DefaultNamespace;

public class Card: BaseEntity
{
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int Position { get; set; }
    public Guid ListId { get; set; }
    public bool Status { get; set; }
    public string? Cover { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? ReminderDate { get; set; }
    public bool IsArchived { get; set; }
    public List<Label> Labels { get; set; } = new List<Label>();
}
