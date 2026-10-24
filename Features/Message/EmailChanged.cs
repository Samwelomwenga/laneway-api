using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed record EmailChanged(
    string OldEmail, string NewEmail, DateTime At, string? UserAgent, string TimeZone) : IMessage
{
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;

    MessageType IMessage.Type => MessageType.EmailChanged;

    Guid? IMessage.UserId => null;

    string? IMessage.Address => OldEmail;

    string? IMessage.Secret => Token;
}
