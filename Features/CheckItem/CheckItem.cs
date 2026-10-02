namespace Laneway.Api;

public class CheckItem : PlacedEntity
{
    public required string Name { get; set; }
    public required Guid ChecklistId { get; set; }
    public bool IsChecked { get; set; }
}
