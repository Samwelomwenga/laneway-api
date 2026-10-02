using System.Text.Json;

namespace Laneway.Api;

public enum CopyJobKind
{
    List,
    Board
}

public enum CopyJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public class CopyJob
{
    public Guid Id { get; set; }
    public required CopyJobKind Kind { get; set; }
    public required Guid SourceId { get; set; }
    public required JsonDocument Request { get; set; }
    public CopyJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public Guid? ResultId { get; set; }
    public JsonDocument? Errors { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
