using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class DeliverySweep(ApplicationDbContext database, TimeProvider time)
{
    public const int BatchSize = 1000;

    public static TimeSpan DeliveryLife { get; } = TimeSpan.FromDays(30);

    public async Task<int> RunAsync(CancellationToken token)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - DeliveryLife;
        var old = database.Deliveries.Where(d => d.CreatedAt < cutoff);
        if (!await old.AnyAsync(token))
        {
            return 0;
        }

        using var span = WorkerSpans.Message.StartRoot(nameof(DeliverySweep));
        var total = 0;
        while (true)
        {
            var deleted = await old
                .OrderBy(d => d.Id)
                .Take(BatchSize)
                .ExecuteDeleteAsync(token);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }
}
