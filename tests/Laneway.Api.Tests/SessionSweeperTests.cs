using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SessionSweeperTests : ApiTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public SessionSweeperTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task An_ended_session_goes_30_days_after_it_ended()
    {
        var ended = (await SeedSessionsAsync(1))[0];
        Time.Advance(TimeSpan.FromDays(1));
        await EndAsync(ended);

        Time.Advance(TimeSpan.FromDays(30) - Minute);
        Assert.Equal(0, await SweepAsync());
        Time.Advance(2 * Minute);
        Assert.Equal(1, await SweepAsync());

        Assert.DoesNotContain(ended, await SessionIdsAsync());
        Assert.Contains(ActorUser.SessionId, await SessionIdsAsync());
        Assert.DoesNotContain(ended, await FromDatabaseAsync(database =>
            database.RefreshTokens.Select(t => t.SessionId).ToListAsync()));
    }

    [Fact]
    public async Task An_idle_session_goes_30_days_after_its_7_idle_days()
    {
        var idle = (await SeedSessionsAsync(1))[0];
        var live = (await SeedSessionsAsync(1))[0];

        for (var day = 0; day < 36; day++)
        {
            Time.Advance(TimeSpan.FromDays(1));
            await TouchAsync(live);
        }

        Time.Advance(TimeSpan.FromDays(1) - Minute);
        Assert.Contains(idle, await SweptAsync());
        Time.Advance(2 * Minute);
        Assert.DoesNotContain(idle, await SweptAsync());
        Assert.Contains(live, await SessionIdsAsync());
    }

    [Fact]
    public async Task An_expired_session_goes_30_days_after_its_30_day_limit()
    {
        var expired = (await SeedSessionsAsync(1))[0];
        for (var day = 0; day < 59; day++)
        {
            Time.Advance(TimeSpan.FromDays(1));
            await TouchAsync(expired);
        }

        Time.Advance(TimeSpan.FromDays(1) - Minute);
        await TouchAsync(expired);
        Assert.Contains(expired, await SweptAsync());
        Time.Advance(2 * Minute);
        Assert.DoesNotContain(expired, await SweptAsync());
    }

    [Fact]
    public async Task The_sweeper_deletes_in_batches_until_none_are_left()
    {
        var sessions = await SeedSessionsAsync(SessionSweeper.BatchSize + 5);
        Time.Advance(TimeSpan.FromDays(1));
        await FromDatabaseAsync(database => database.Sessions
            .Where(s => sessions.Contains(s.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)));
        Time.Advance(TimeSpan.FromDays(30) + Minute);
        await TouchAsync(ActorUser.SessionId);

        Assert.Equal(SessionSweeper.BatchSize + 5, await SweepAsync());
        Assert.Equal([ActorUser.SessionId], await SessionIdsAsync());
    }

    private Task<int> SweepAsync() =>
        Services.GetRequiredService<SessionSweeper>().SweepAsync(CancellationToken.None);

    private async Task<List<Guid>> SweptAsync()
    {
        await SweepAsync();
        return await SessionIdsAsync();
    }

    private Task<List<Guid>> SessionIdsAsync() =>
        FromDatabaseAsync(database => database.Sessions.Select(s => s.Id).ToListAsync());

    private Task EndAsync(Guid sessionId) =>
        FromDatabaseAsync(database => database.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.EndReason, SessionEndReason.SignedOut)));

    private Task TouchAsync(Guid sessionId) =>
        FromDatabaseAsync(database => database.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, Time.GetUtcNow().UtcDateTime)));

    private async Task<List<Guid>> SeedSessionsAsync(int count)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        var sessions = Enumerable.Range(0, count)
            .Select(_ => Session.Start(ActorId, "Laneway.Api.Tests", now))
            .ToList();
        await FromDatabaseAsync(database =>
        {
            database.Sessions.AddRange(sessions);
            database.RefreshTokens.AddRange(sessions.Select(session => RefreshToken.Issue(session.Id, now).Row));
            return database.SaveChangesAsync();
        });
        return sessions.ConvertAll(session => session.Id);
    }
}
