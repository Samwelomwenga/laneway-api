using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ChangeEmailTests : ApiTests
{
    public ChangeEmailTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Asking_emails_the_new_address_a_confirm_link_and_keeps_the_old_one()
    {
        var newEmail = NewEmail();

        using var response = await ChangeEmailAsync(newEmail);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var send = await AwaitEmailAsync(newEmail);
        Assert.Equal("Confirm your new Laneway email", send.Subject);
        Assert.Contains(
            $"The Laneway account @{ActorUser.User.Username} asked to use this address for signing in.",
            send.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "The link works for 24 hours. If you didn't ask for this, ignore this email and nothing changes.",
            send.Text,
            StringComparison.Ordinal);
        Assert.NotEmpty(send.TokenFrom(AppRoutes.ConfirmEmailChange));
        Assert.Equal(["ConfirmEmailChange"], send.Tags);
        Assert.Equal(ActorUser.User.Email, await EmailOfAsync(ActorId));
    }

    [Fact]
    public async Task Confirming_moves_the_address_and_signs_nobody_in()
    {
        var newEmail = NewEmail();
        var token = await ConfirmTokenAsync(newEmail);

        using var response = await ConfirmAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(newEmail, await EmailOfAsync(ActorId));
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var changed = Assert.Single(Logs(), log => log.Id.Name == "EmailChanged");
        Assert.Equal(ActorId.ToString(), changed.GetStructuredStateValue("sub"));
        Assert.DoesNotContain(newEmail, changed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirming_emails_the_old_address_a_masked_notice_with_an_undo_link()
    {
        var oldEmail = ActorUser.User.Email;
        var token = await ConfirmTokenAsync("jane.doe@gmail.com");

        using var response = await ConfirmAsync(token);

        var notice = await AwaitEmailAsync(oldEmail);
        Assert.Equal("Your Laneway email was changed", notice.Subject);
        Assert.Contains("Your Laneway email was changed to j•••@g•••.com on ", notice.Text, StringComparison.Ordinal);
        Assert.Contains("We'll no longer send mail here.", notice.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("jane.doe@gmail.com", notice.Text, StringComparison.Ordinal);
        Assert.NotEmpty(notice.TokenFrom(AppRoutes.UndoEmailChange));
        Assert.Equal(["EmailChanged"], notice.Tags);
        var undo = await FromDatabaseAsync(database =>
            database.AuthTokens.AsNoTracking().SingleAsync(t => t.Kind == AuthTokenKind.Undo));
        Assert.Equal(oldEmail, undo.Email);
        Assert.Equal(AuthToken.UndoLifetime, undo.ExpiresAt - undo.CreatedAt);
    }

    [Fact]
    public async Task Confirming_an_address_an_unverified_user_has_deletes_that_user()
    {
        var newEmail = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(newEmail));
        await AwaitEmailAsync(newEmail);
        using var request = await ChangeEmailAsync(newEmail);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = (await Brevo.AwaitSendsAsync(2))[1].TokenFrom(AppRoutes.ConfirmEmailChange);

        using var response = await ConfirmAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var holders = await FromDatabaseAsync(database =>
            database.Users.IgnoreQueryFilters().Where(u => u.Email == newEmail).Select(u => u.Id).ToListAsync());
        Assert.Equal([ActorId], holders);
    }

    [Fact]
    public async Task Confirming_an_address_a_verified_user_took_since_answers_409_and_changes_nothing()
    {
        var newEmail = NewEmail();
        var token = await ConfirmTokenAsync(newEmail);
        var other = await CreateUserAsync();
        await FromDatabaseAsync(database => database.Users
            .Where(u => u.Id == other.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Email, newEmail)));

        using var response = await ConfirmAsync(token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal((null, ErrorCodes.Duplicate), (error.Field, error.Code));
        Assert.Equal(ActorUser.User.Email, await EmailOfAsync(ActorId));
        Assert.False(await FromDatabaseAsync(database =>
            database.Deliveries.AnyAsync(d => d.Type == MessageType.EmailChanged)));
    }

    [Fact]
    public async Task A_used_link_stops_working()
    {
        var token = await ConfirmTokenAsync(NewEmail());
        using var first = await ConfirmAsync(token);

        using var second = await ConfirmAsync(token);

        await AssertInvalidTokenAsync(second);
    }

    [Fact]
    public async Task A_bad_token_answers_invalidToken()
    {
        using var response = await ConfirmAsync("not-a-real-token");

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_link_lasts_24_hours()
    {
        var token = await ConfirmTokenAsync(NewEmail());
        Time.Advance(TimeSpan.FromHours(24));

        using var response = await ConfirmAsync(token);

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_newer_request_replaces_the_old_link()
    {
        var first = await ConfirmTokenAsync(NewEmail());
        var secondEmail = NewEmail();
        var second = await ConfirmTokenAsync(secondEmail);

        using var stale = await ConfirmAsync(first);
        using var fresh = await ConfirmAsync(second);

        await AssertInvalidTokenAsync(stale);
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
        Assert.Equal(secondEmail, await EmailOfAsync(ActorId));
    }

    [Fact]
    public async Task Asking_for_the_current_address_cancels_the_pending_change_and_sends_nothing()
    {
        var token = await ConfirmTokenAsync(NewEmail());

        using var response = await ChangeEmailAsync(ActorUser.User.Email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var confirm = await ConfirmAsync(token);
        await AssertInvalidTokenAsync(confirm);
        Assert.Equal(1, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
    }

    [Fact]
    public async Task Confirming_voids_a_live_reset_link()
    {
        using var forgot = await ForgotPasswordAsync(ActorUser.User.Email);
        var reset = (await AwaitEmailAsync(ActorUser.User.Email)).TokenFrom(AppRoutes.ResetPassword);
        var token = await ConfirmTokenAsync(NewEmail());

        using var response = await ConfirmAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var resetResponse = await ResetPasswordAsync(reset, "quiet harbor lantern");
        await AssertInvalidTokenAsync(resetResponse);
    }

    [Fact]
    public async Task A_reset_cancels_a_pending_change()
    {
        var token = await ConfirmTokenAsync(NewEmail());
        using var forgot = await ForgotPasswordAsync(ActorUser.User.Email);
        var reset = (await AwaitEmailAsync(ActorUser.User.Email)).TokenFrom(AppRoutes.ResetPassword);
        using var resetResponse = await ResetPasswordAsync(reset, "quiet harbor lantern");
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        using var response = await ConfirmAsync(token);

        await AssertInvalidTokenAsync(response);
        Assert.Equal(ActorUser.User.Email, await EmailOfAsync(ActorId));
    }

    [Fact]
    public async Task The_sixth_confirm_email_to_one_address_in_24_hours_changes_nothing_and_warns()
    {
        var newEmail = NewEmail();
        var others = new List<TestUser>();
        for (var ask = 0; ask < 5; ask++)
        {
            var other = await CreateUserAsync();
            others.Add(other);
            using var sent = await ChangeEmailAsync(newEmail, other);
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        await Brevo.AwaitSendsAsync(5);
        var pending = await ConfirmTokenAsync(NewEmail());

        using var capped = await ChangeEmailAsync(newEmail);

        Assert.Equal(HttpStatusCode.Accepted, capped.StatusCode);
        Assert.Equal(5, await FromDatabaseAsync(database => database.Deliveries.CountAsync(d => d.Address == newEmail)));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal(LogLevel.Warning, skipped.Level);
        Assert.Equal("ConfirmEmailChange", skipped.GetStructuredStateValue("messageType"));
        Assert.Equal("addressCapped", skipped.GetStructuredStateValue("reason"));
        Assert.Equal(ActorId.ToString(), skipped.GetStructuredStateValue("sub"));
        using var confirm = await ConfirmAsync(pending);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    [Fact]
    public async Task A_suppressed_address_answers_the_same_and_sends_nothing()
    {
        var newEmail = NewEmail();
        await SuppressAsync(newEmail);

        using var response = await ChangeEmailAsync(newEmail);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(await FromDatabaseAsync(database => database.Deliveries.AnyAsync()));
        Assert.False(await FromDatabaseAsync(database => database.AuthTokens.AnyAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal("ConfirmEmailChange", skipped.GetStructuredStateValue("messageType"));
        Assert.Equal("suppressed", skipped.GetStructuredStateValue("reason"));
    }

    private async Task<string> ConfirmTokenAsync(string newEmail)
    {
        using var response = await ChangeEmailAsync(newEmail);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await AwaitEmailAsync(newEmail)).TokenFrom(AppRoutes.ConfirmEmailChange);
    }

    private Task<HttpResponseMessage> ConfirmAsync(string token) =>
        SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/confirm-email-change", new { token });

    private Task<HttpResponseMessage> ChangeEmailAsync(string newEmail, TestUser? @as = null) =>
        PostAsync("/api/v1/auth/change-email", new { password = TestUser.Password, newEmail }, @as);

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("token", ErrorCodes.InvalidToken), (error.Field, error.Code));
    }

    private Task<string> EmailOfAsync(Guid userId) =>
        FromDatabaseAsync(database => database.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync());
}
