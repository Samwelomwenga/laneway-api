namespace DefaultNamespace;

public class Checklist : PlacedEntity, IArchivable
{
    public required string Name { get; set; }
    public required Guid CardId { get; set; }
    public bool IsArchived { get; set; }
    public List<CheckItem> CheckItems { get; set; } = new List<CheckItem>();
}
