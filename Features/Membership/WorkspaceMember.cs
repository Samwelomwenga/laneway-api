namespace Laneway.Api;

public class WorkspaceMember : BaseEntity
{
    public required Guid WorkspaceId { get; set; }
    public required Guid UserId { get; set; }
    public required WorkspaceRole Role { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;
}
