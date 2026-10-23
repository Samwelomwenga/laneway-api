using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class DeliverySweepTests : ApiTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public DeliverySweepTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_delivery_goes_30_days_after_it_was_created()
    {
        var id = (await SeedSentAsync(1, Time.GetUtcNow().UtcDateTime))[0];

        Time.Advance(TimeSpan.FromDays(30) - Minute);
        await SweepAsync();
        Assert.Equal([id], await DeliveryIdsAsync());
        Time.Advance(2 * Minute);
        await SweepAsync();
        Assert.Empty(await DeliveryIdsAsync());
    }

    [Fact]
    public async Task The_sweep_deletes_past_one_batch()
    {
        var now = Time.GetUtcNow().UtcDateTime;
        await SeedSentAsync(DeliverySweep.BatchSize + 1, now - TimeSpan.FromDays(31));
        var kept = (await SeedSentAsync(1, now - TimeSpan.FromDays(29)))[0];

        await SweepAsync();

        Assert.Equal([kept], await DeliveryIdsAsync());
    }

    private Task<List<Guid>> DeliveryIdsAsync() =>
        FromDatabaseAsync(database => database.Deliveries.Select(d => d.Id).ToListAsync());

    private async Task SweepAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliverySweep>().RunAsync(CancellationToken.None);
    }

    private Task<List<Guid>> SeedSentAsync(int count, DateTime createdAt) =>
        FromDatabaseAsync(async database =>
        {
            var rows = Enumerable.Range(0, count).Select(_ => new Delivery
            {
                Id = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                Type = MessageType.PasswordChanged,
                Channel = MessageChannel.Email,
                Message = MessageBodies.Of(PasswordNotice()),
                UserId = ActorId,
                Status = DeliveryStatus.Sent,
                IdempotencyKey = Guid.NewGuid(),
                CreatedAt = createdAt,
                NextAttemptAt = createdAt,
                SendBy = createdAt.AddHours(24)
            }).ToList();
            database.Deliveries.AddRange(rows);
            await database.SaveChangesAsync();
            return rows.ConvertAll(row => row.Id);
        });
}
