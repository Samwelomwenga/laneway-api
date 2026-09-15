namespace DefaultNamespace;

public class Label: BaseEntity
{
    public required string Name { get; set; }
    public Color? Color { get; set; }
    public List<Card> Cards { get; set; } = new List<Card>();
    
}
