namespace Laneway.Api;

public sealed record InviteSent(
    string Email, Guid InviterId, string InviterName, string InviterUsername, InvitePlace Place, string PlaceName)
    : IMessage
{
    MessageType IMessage.Type => MessageType.InviteSent;

    Guid? IMessage.UserId => null;

    string? IMessage.Address => Email;

    string? IMessage.Secret => null;
}
