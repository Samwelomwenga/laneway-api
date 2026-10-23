using System.Text.Json;

namespace Laneway.Api;

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Dropped
}

public class Delivery
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public MessageType Type { get; set; }
    public MessageChannel Channel { get; set; }
    public required JsonDocument Message { get; set; }
    public Guid? UserId { get; set; }
    public string? Address { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime SendBy { get; set; }
    public string? ProviderMessageId { get; set; }
}
