using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthSweeperTests : ApiTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public AuthSweeperTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_token_goes_once_it_expires()
    {
        var expiring = await SeedTokenAsync();
        Time.Advance(TimeSpan.FromHours(12));
        var live = await SeedTokenAsync();

        Time.Advance(TimeSpan.FromHours(12) - Minute);
        await SweepAsync();
        Assert.Contains(expiring, await TokenIdsAsync());
        Time.Advance(Minute);
        await SweepAsync();

        Assert.Equal([live], await TokenIdsAsync());
    }

    [Fact]
    public async Task A_user_still_unverified_7_days_after_sign_up_is_deleted_with_their_tokens()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var userId = await UserIdAsync(email);

        Time.Advance(AuthSweeper.UnverifiedLife - Minute);
        await SweepAsync();
        Assert.NotNull(await UserIdAsync(email));
        Time.Advance(Minute);
        await SweepAsync();

        Assert.Null(await UserIdAsync(email));
        Assert.False(await FromDatabaseAsync(database => database.AuthTokens.AnyAsync(t => t.UserId == userId)));
        Assert.Contains(ActorId, await FromDatabaseAsync(database => database.Users.Select(u => u.Id).ToListAsync()));
    }

    [Fact]
    public async Task A_verified_user_is_never_swept()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        using var verify = await VerifyEmailAsync(await VerifyTokenSentToAsync(email));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        Time.Advance(AuthSweeper.UnverifiedLife + Minute);
        await SweepAsync();

        Assert.NotNull(await UserIdAsync(email));
    }

    [Fact]
    public async Task A_takeover_starts_the_7_days_again()
    {
        var email = NewEmail();
        using var squatter = await SignUpAsync(SignUpBody(email, username: "squatter"));
        Time.Advance(TimeSpan.FromDays(6));
        using var owner = await SignUpAsync(SignUpBody(email, username: "owner"));

        Time.Advance(TimeSpan.FromDays(1) + Minute);
        await SweepAsync();

        Assert.NotNull(await UserIdAsync(email));
    }

    [Fact]
    public async Task The_sweeper_deletes_in_batches_until_none_are_left()
    {
        var now = Time.GetUtcNow().UtcDateTime;
        await FromDatabaseAsync(database =>
        {
            database.AuthTokens.AddRange(Enumerable.Range(0, AuthSweeper.BatchSize + 5).Select(_ =>
                AuthToken.Issue(ActorId, AuthTokenKind.Verification, AuthToken.VerificationLifetime, now).Row));
            return database.SaveChangesAsync();
        });
        Time.Advance(AuthToken.VerificationLifetime);

        await SweepAsync();

        Assert.Empty(await TokenIdsAsync());
    }

    private Task SweepAsync() =>
        Services.GetRequiredService<AuthSweeper>().SweepAsync(CancellationToken.None);

    private Task<List<Guid>> TokenIdsAsync() =>
        FromDatabaseAsync(database => database.AuthTokens.Select(t => t.Id).ToListAsync());

    private Task<Guid?> UserIdAsync(string email) =>
        FromDatabaseAsync(database =>
            database.Users.Where(u => u.Email == email).Select(u => (Guid?)u.Id).SingleOrDefaultAsync());

    private async Task<Guid> SeedTokenAsync()
    {
        var (row, _) = AuthToken.Issue(
            ActorId, AuthTokenKind.Verification, AuthToken.VerificationLifetime, Time.GetUtcNow().UtcDateTime);
        await FromDatabaseAsync(database =>
        {
            database.AuthTokens.Add(row);
            return database.SaveChangesAsync();
        });
        return row.Id;
    }
}
