using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed record ConfirmEmailChange(string Username, string NewEmail) : IMessage
{
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;

    MessageType IMessage.Type => MessageType.ConfirmEmailChange;

    Guid? IMessage.UserId => null;

    string? IMessage.Address => NewEmail;

    string? IMessage.Secret => Token;
}
