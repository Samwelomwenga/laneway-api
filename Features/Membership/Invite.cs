namespace Laneway.Api;

public enum InvitePlace
{
    Workspace,
    Board
}

public class Invite
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? BoardId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public Guid InvitedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime EmailedAt { get; set; }
}
