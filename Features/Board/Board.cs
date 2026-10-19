namespace Laneway.Api;

public class Board : BaseEntity, IArchivable
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required Guid WorkspaceId { get; set; }
    public required BoardVisibility Visibility { get; set; }
    public bool IsArchived { get; set; }
    public BoardCommenting Commenting { get; set; } = BoardCommenting.Members;
    public BoardAddingMembers AddingMembers { get; set; } = BoardAddingMembers.Admins;
    public bool SelfJoin { get; set; } = true;
    public List<List> Lists { get; set; } = new List<List>();

}
