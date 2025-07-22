namespace DefaultNamespace;

public class label: BaseEntity
{
    public required string Name { get; set; }
    public required string Color { get; set; }
    public List<Card> Cards { get; set; } = new List<Card>();
    
}
