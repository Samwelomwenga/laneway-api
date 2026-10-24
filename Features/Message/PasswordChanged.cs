namespace Laneway.Api;

public enum PasswordChangeHow
{
    Changed,
    Reset
}

public sealed record PasswordChanged(
    Guid UserId, PasswordChangeHow How, DateTime At, string? UserAgent, string TimeZone) : IMessage
{
    MessageType IMessage.Type => MessageType.PasswordChanged;

    Guid? IMessage.UserId => UserId;

    string? IMessage.Address => null;

    string? IMessage.Secret => null;
}
