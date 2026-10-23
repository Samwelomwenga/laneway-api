namespace Laneway.Api;

/// <summary>
/// One kind of message. A record names its recipient by <see cref="UserId"/> when the message is about the person,
/// resolved to an address at send time, or by <see cref="Address"/> when the address is the point.
/// </summary>
public interface IMessage
{
    MessageType Type { get; }

    Guid? UserId { get; }

    string? Address { get; }
}
