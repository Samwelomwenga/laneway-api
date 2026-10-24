using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed record ResetPassword(Guid UserId, string Username) : IMessage
{
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;

    MessageType IMessage.Type => MessageType.ResetPassword;

    Guid? IMessage.UserId => UserId;

    string? IMessage.Address => null;

    string? IMessage.Secret => Token;
}
