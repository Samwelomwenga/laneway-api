namespace DefaultNamespace;

public class List : PlacedEntity, IArchivable
{
    public required string Name { get; set; }
    public required Guid BoardId { get; set; }
    public Color? Color { get; set; }
    public bool IsArchived { get; set; }
    public List<Card> Cards { get; set; } = new List<Card>();
}
