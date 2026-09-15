namespace DefaultNamespace;

public class Board: BaseEntity
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required Guid WorkspaceId { get; set; }
    public required BoardVisibility Visibility { get; set; }
    public bool IsArchived { get; set; }
    public List<List> Lists { get; set; } = new List<List>();
    
}
