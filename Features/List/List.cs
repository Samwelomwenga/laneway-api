namespace DefaultNamespace;

public class List: BaseEntity
{
    public required string Name { get; set; }
    public int Position { get; set; }
    public required Guid BoardId { get; set; }
    public string Color { get; set; }
    public bool IsArchived { get; set; }
    public List<Card> Cards { get; set; } = new List<Card>();
}
