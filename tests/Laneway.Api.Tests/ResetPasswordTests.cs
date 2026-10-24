using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ResetPasswordTests : ApiTests
{
    private const string NewPassword = "quiet harbor lantern";

    public ResetPasswordTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_reset_swaps_the_password_and_signs_nobody_in()
    {
        var token = await ResetTokenAsync();

        using var response = await ResetPasswordAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var oldPassword = await SignInAsync(TestUser.Password);
        using var newPassword = await SignInAsync(NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task A_reset_ends_every_session()
    {
        var token = await ResetTokenAsync();

        using var response = await ResetPasswordAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        var session = await FromDatabaseAsync(database =>
            database.Sessions.AsNoTracking().SingleAsync(s => s.Id == ActorUser.SessionId));
        Assert.Equal(SessionEndReason.PasswordReset, session.EndReason);
    }

    [Fact]
    public async Task A_reset_lifts_the_sign_in_block()
    {
        var token = await ResetTokenAsync();
        var now = Time.GetUtcNow().UtcDateTime;
        await FromDatabaseAsync(database => database.Users.Where(u => u.Id == ActorId).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.FailedSignInCount, 10)
            .SetProperty(e => e.LastFailedSignInAt, now)
            .SetProperty(e => e.SignInBlockedUntil, now.AddHours(1))));

        using var response = await ResetPasswordAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var signIn = await SignInAsync(NewPassword);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task A_used_link_stops_working()
    {
        var token = await ResetTokenAsync();
        using var first = await ResetPasswordAsync(token, NewPassword);

        using var second = await ResetPasswordAsync(token, "violet canyon whistle");

        await AssertInvalidTokenAsync(second);
    }

    [Fact]
    public async Task A_newer_link_replaces_the_old_one()
    {
        var first = await ResetTokenAsync();
        using var again = await ForgotPasswordAsync(ActorUser.User.Email);
        var second = (await Brevo.AwaitSendsAsync(2))[1].TokenFrom(AppRoutes.ResetPassword);

        using var stale = await ResetPasswordAsync(first, NewPassword);
        using var fresh = await ResetPasswordAsync(second, NewPassword);

        await AssertInvalidTokenAsync(stale);
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
    }

    [Fact]
    public async Task A_link_lasts_one_hour()
    {
        var token = await ResetTokenAsync();
        Time.Advance(TimeSpan.FromHours(1));

        using var response = await ResetPasswordAsync(token, NewPassword);

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_bad_token_is_checked_before_the_password()
    {
        using var response = await ResetPasswordAsync("not-a-real-token", "my laneway password");

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_bad_token_with_a_short_password_is_still_an_invalid_token()
    {
        using var response = await ResetPasswordAsync("not-a-real-token", "short");

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_short_password_answers_400_and_leaves_the_link_live()
    {
        var token = await ResetTokenAsync();

        using var refused = await ResetPasswordAsync(token, "short");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Equal(("newPassword", ErrorCodes.TooShort), (error.Field, error.Code));
        using var accepted = await ResetPasswordAsync(token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task A_refused_password_answers_400_and_leaves_the_link_live()
    {
        var token = await ResetTokenAsync();

        using var refused = await ResetPasswordAsync(token, ActorUser.User.Username + " plum");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Equal(("newPassword", ErrorCodes.ContainsContextWord), (error.Field, error.Code));
        using var accepted = await ResetPasswordAsync(token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task A_reset_emails_a_notice()
    {
        var token = await ResetTokenAsync();

        using var response = await ResetPasswordAsync(token, NewPassword);

        var notice = (await Brevo.AwaitSendsAsync(2))[1];
        Assert.Equal("Your Laneway password was reset", notice.Subject);
        Assert.Contains("using a link sent to this email. We signed you out everywhere.", notice.Text, StringComparison.Ordinal);
        Assert.Contains($"{ApiFactory.AllowedOrigin}/forgot-password", notice.Text, StringComparison.Ordinal);
        Assert.Equal(["PasswordChanged"], notice.Tags);
    }

    [Fact]
    public async Task A_reset_keeps_the_replaced_hash_in_password_history()
    {
        var oldHash = await FromDatabaseAsync(database =>
            database.Users.Where(u => u.Id == ActorId).Select(u => u.PasswordHash).SingleAsync());
        var token = await ResetTokenAsync();

        using var response = await ResetPasswordAsync(token, NewPassword);

        var history = await FromDatabaseAsync(database => database.PasswordHistory.AsNoTracking().ToListAsync());
        var row = Assert.Single(history);
        Assert.Equal(ActorId, row.UserId);
        Assert.Equal(oldHash, row.PasswordHash);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, row.ReplacedAt, TimeSpan.FromMilliseconds(1));
    }

    private async Task<string> ResetTokenAsync()
    {
        using var response = await ForgotPasswordAsync(ActorUser.User.Email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await AwaitEmailAsync(ActorUser.User.Email)).TokenFrom(AppRoutes.ResetPassword);
    }

    private Task<HttpResponseMessage> SignInAsync(string password) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password });

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("token", ErrorCodes.InvalidToken), (error.Field, error.Code));
    }
}
