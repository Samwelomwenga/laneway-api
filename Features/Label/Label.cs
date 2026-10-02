namespace Laneway.Api;

public class Label : BaseEntity
{
    public required string Name { get; set; }
    public required Guid BoardId { get; set; }
    public Color? Color { get; set; }
    public List<Card> Cards { get; set; } = new List<Card>();

}
