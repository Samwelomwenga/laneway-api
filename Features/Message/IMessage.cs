namespace Laneway.Api;

/// <summary>
/// One kind of message. A record names its recipient by <see cref="UserId"/> when the message is about the person,
/// resolved to an address at send time, or by <see cref="Address"/> when the address is the point. A raw token goes in
/// <see cref="Secret"/>, which the delivery keeps out of its stored body and clears once it ends.
/// </summary>
public interface IMessage
{
    MessageType Type { get; }

    Guid? UserId { get; }

    string? Address { get; }

    string? Secret { get; }
}
