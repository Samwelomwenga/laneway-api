using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed record VerifyEmail(Guid UserId) : IMessage
{
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;

    MessageType IMessage.Type => MessageType.VerifyEmail;

    Guid? IMessage.UserId => UserId;

    string? IMessage.Address => null;

    string? IMessage.Secret => Token;
}
