namespace Laneway.Api;

public sealed record EmailChangeUndone(string ReplacedEmail, DateTime At, string TimeZone) : IMessage
{
    MessageType IMessage.Type => MessageType.EmailChangeUndone;

    Guid? IMessage.UserId => null;

    string? IMessage.Address => ReplacedEmail;

    string? IMessage.Secret => null;
}
