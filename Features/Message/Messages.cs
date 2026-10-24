namespace Laneway.Api;

public sealed class Messages(ApplicationDbContext database, TimeProvider time)
{
    /// <summary>
    /// Adds one delivery per channel the message's route lists to the caller's context. The caller's
    /// <c>SaveChanges</c> commits them, and nothing goes out if its transaction rolls back.
    /// </summary>
    public void Add(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var route = MessageRoutes.For(message.Type);
        var now = time.GetUtcNow().UtcDateTime;
        var groupId = Guid.NewGuid();
        foreach (var channel in route.Channels)
        {
            database.Deliveries.Add(new Delivery
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Type = message.Type,
                Channel = channel,
                Message = MessageBodies.Of(message),
                UserId = message.UserId,
                Address = message.Address,
                Secret = message.Secret,
                Status = DeliveryStatus.Pending,
                IdempotencyKey = Guid.NewGuid(),
                CreatedAt = now,
                NextAttemptAt = now,
                SendBy = now + route.SendBy
            });
        }
    }
}
