namespace DefaultNamespace;

public class WorkSpace : BaseEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; } = string.Empty;
    public required string Visibility { get; set; } 
    public bool IsArchived { get; set; }
    public List<Board> Boards { get; set; } = new List<Board>();
}
