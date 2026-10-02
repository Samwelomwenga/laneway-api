using System.Text.Json;

namespace Laneway.Api;

public class ActivityEntry
{
    public Guid Id { get; set; }
    public required ActivityType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? BoardId { get; set; }
    public Guid? ListId { get; set; }
    public Guid? CardId { get; set; }
    public Guid? FromWorkspaceId { get; set; }
    public Guid? FromBoardId { get; set; }
    public Guid? FromListId { get; set; }
    public required JsonDocument Data { get; set; }
    public string? Text { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
