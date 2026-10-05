namespace Laneway.Api;

public class WorkSpace : BaseEntity
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required WorkspaceVisibility Visibility { get; set; }
    public List<Board> Boards { get; set; } = new List<Board>();
}
