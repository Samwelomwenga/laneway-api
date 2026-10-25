using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class UndoEmailChangeTests : ApiTests
{
    private const string NewPassword = "quiet harbor lantern";

    public UndoEmailChangeTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Undoing_restores_the_address_and_sets_the_new_password()
    {
        var token = await UndoTokenAsync(NewEmail());

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(ActorUser.User.Email, await EmailOfAsync(ActorId));
        using var oldPassword = await SignInAsync(TestUser.Password);
        using var newPassword = await SignInAsync(NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task A_password_changed_after_the_email_change_still_counts_as_recent()
    {
        var token = await UndoTokenAsync(NewEmail());
        using var changed = await PostAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = TestUser.Password, newPassword = "violet canyon whistle" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_password_replaced_more_than_7_days_before_the_change_is_not_recent()
    {
        using var changed = await PostAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = TestUser.Password, newPassword = "violet canyon whistle" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var longAgo = Time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1);
        await FromDatabaseAsync(database => database.PasswordHistory
            .ExecuteUpdateAsync(h => h.SetProperty(e => e.ReplacedAt, longAgo)));
        var token = await UndoTokenAsync(NewEmail(), "violet canyon whistle");

        using var stale = await UndoAsync(token, TestUser.Password, NewPassword);
        using var current = await UndoAsync(token, "violet canyon whistle", NewPassword);

        await AssertWrongPasswordAsync(stale);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task A_wrong_recent_password_answers_400_and_counts_toward_the_sign_in_block()
    {
        var token = await UndoTokenAsync(NewEmail());

        using var response = await UndoAsync(token, "not my password at all", NewPassword);

        await AssertWrongPasswordAsync(response);
        Assert.Equal(1, await FromDatabaseAsync(database =>
            database.Users.Where(u => u.Id == ActorId).Select(u => u.FailedSignInCount).SingleAsync()));
        var failed = Assert.Single(Logs(), log => log.Id.Name == "PasswordCheckFailed");
        Assert.Equal(ActorId.ToString(), failed.GetStructuredStateValue("sub"));
        Assert.Null(failed.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task The_fifth_wrong_recent_password_burns_the_link()
    {
        var token = await UndoTokenAsync(NewEmail());
        for (var wrong = 0; wrong < 4; wrong++)
        {
            using var miss = await UndoAsync(token, "not my password at all", NewPassword);
            await AssertWrongPasswordAsync(miss);
        }

        using var fifth = await UndoAsync(token, "not my password at all", NewPassword);
        using var right = await UndoAsync(token, TestUser.Password, NewPassword);

        await AssertWrongPasswordAsync(fifth);
        await AssertInvalidTokenAsync(right);
    }

    [Fact]
    public async Task Parallel_wrong_recent_passwords_get_no_more_than_five_checks()
    {
        var token = await UndoTokenAsync(NewEmail());

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => UndoAsync(token, "not my password at all", NewPassword)));

        var codes = new List<string?>();
        foreach (var response in responses)
        {
            using (response)
            {
                codes.Add(Assert.Single(await response.ReadErrors()).Code);
            }
        }

        Assert.Equal(5, codes.Count(code => code == ErrorCodes.WrongPassword));
        Assert.Equal(5, codes.Count(code => code == ErrorCodes.InvalidToken));
        using var right = await UndoAsync(token, TestUser.Password, NewPassword);
        await AssertInvalidTokenAsync(right);
    }

    [Fact]
    public async Task Five_refused_new_passwords_leave_the_link_live()
    {
        var token = await UndoTokenAsync(NewEmail());
        for (var refusal = 0; refusal < 5; refusal++)
        {
            using var refused = await UndoAsync(token, TestUser.Password, "short");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        using var accepted = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Four_wrong_recent_passwords_leave_the_link_live()
    {
        var token = await UndoTokenAsync(NewEmail());
        for (var wrong = 0; wrong < 4; wrong++)
        {
            using var miss = await UndoAsync(token, "not my password at all", NewPassword);
            await AssertWrongPasswordAsync(miss);
        }

        using var right = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
    }

    [Fact]
    public async Task The_sign_in_block_never_stops_an_undo_and_the_undo_ends_it()
    {
        var token = await UndoTokenAsync(NewEmail());
        var now = Time.GetUtcNow().UtcDateTime;
        await FromDatabaseAsync(database => database.Users.Where(u => u.Id == ActorId).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.FailedSignInCount, 10)
            .SetProperty(e => e.LastFailedSignInAt, now)
            .SetProperty(e => e.SignInBlockedUntil, now.AddHours(1))));

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var signIn = await SignInAsync(NewPassword);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task A_refused_new_password_answers_400_and_leaves_the_link_live()
    {
        var token = await UndoTokenAsync(NewEmail());

        using var refused = await UndoAsync(token, TestUser.Password, ActorUser.User.Username + " plum");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Equal(("newPassword", ErrorCodes.ContainsContextWord), (error.Field, error.Code));
        using var accepted = await UndoAsync(token, TestUser.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Undoing_ends_every_session()
    {
        var token = await UndoTokenAsync(NewEmail());

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        var session = await FromDatabaseAsync(database =>
            database.Sessions.AsNoTracking().SingleAsync(s => s.Id == ActorUser.SessionId));
        Assert.Equal(SessionEndReason.EmailChangeUndone, session.EndReason);
        var undone = Assert.Single(Logs(), log => log.Id.Name == "EmailChangeUndone");
        Assert.Equal(ActorId.ToString(), undone.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Undoing_keeps_the_replaced_hash_in_password_history()
    {
        var token = await UndoTokenAsync(NewEmail());
        var oldHash = await FromDatabaseAsync(database =>
            database.Users.Where(u => u.Id == ActorId).Select(u => u.PasswordHash).SingleAsync());

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(await FromDatabaseAsync(database => database.PasswordHistory.AsNoTracking().ToListAsync()));
        Assert.Equal((ActorId, oldHash), (row.UserId, row.PasswordHash));
    }

    [Fact]
    public async Task Undoing_emails_the_undone_address_and_sends_no_password_notice()
    {
        var newEmail = NewEmail();
        var token = await UndoTokenAsync(newEmail);

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var notice = await Brevo.AwaitEmailAsync(newEmail, "EmailChangeUndone");
        Assert.Equal("Your Laneway email change was undone", notice.Subject);
        Assert.Contains(
            "The change of your Laneway email to this address was undone on ", notice.Text, StringComparison.Ordinal);
        Assert.Contains("stop using that password anywhere else.", notice.Text, StringComparison.Ordinal);
        Assert.False(await FromDatabaseAsync(database =>
            database.Deliveries.AnyAsync(d => d.Type == MessageType.PasswordChanged)));
    }

    [Fact]
    public async Task Undoing_cancels_a_pending_change_and_voids_a_live_reset_link()
    {
        var token = await UndoTokenAsync(NewEmail());
        var pendingEmail = NewEmail();
        using var pending = await PostAsync(
            "/api/v1/auth/change-email", new { password = TestUser.Password, newEmail = pendingEmail });
        var confirmToken = (await AwaitEmailAsync(pendingEmail)).TokenFrom(AppRoutes.ConfirmEmailChange);
        var currentEmail = await EmailOfAsync(ActorId);
        using var forgot = await ForgotPasswordAsync(currentEmail);
        var reset = (await Brevo.AwaitEmailAsync(currentEmail, "ResetPassword")).TokenFrom(AppRoutes.ResetPassword);

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var confirm = await SendAnonymousAsync(
            HttpMethod.Post, "/api/v1/auth/confirm-email-change", new { token = confirmToken });
        await AssertInvalidTokenAsync(confirm);
        using var resetResponse = await ResetPasswordAsync(reset, "violet canyon whistle");
        await AssertInvalidTokenAsync(resetResponse);
    }

    [Fact]
    public async Task An_attacker_changing_twice_cannot_void_the_owners_link()
    {
        var owner = ActorUser.User.Email;
        var middle = NewEmail();
        var ownersLink = await UndoTokenAsync(middle);
        var attackersLink = await UndoTokenAsync(NewEmail());
        Time.Advance(AuthToken.UndoLifetime - TimeSpan.FromMinutes(1));

        using var response = await UndoAsync(ownersLink, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(owner, await EmailOfAsync(ActorId));
        using var other = await UndoAsync(attackersLink, NewPassword, "violet canyon whistle");
        await AssertInvalidTokenAsync(other);
        Assert.NotEqual(middle, await EmailOfAsync(ActorId));
    }

    [Fact]
    public async Task A_link_lasts_7_days()
    {
        var token = await UndoTokenAsync(NewEmail());
        Time.Advance(AuthToken.UndoLifetime);

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task A_used_link_stops_working()
    {
        var token = await UndoTokenAsync(NewEmail());
        using var first = await UndoAsync(token, TestUser.Password, NewPassword);

        using var second = await UndoAsync(token, NewPassword, "violet canyon whistle");

        await AssertInvalidTokenAsync(second);
    }

    [Fact]
    public async Task A_bad_token_is_checked_before_the_passwords()
    {
        using var response = await UndoAsync("not-a-real-token", "wrong", "short");

        await AssertInvalidTokenAsync(response);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "PasswordCheckFailed");
    }

    [Fact]
    public async Task An_address_a_verified_user_took_since_answers_409_and_changes_nothing()
    {
        var newEmail = NewEmail();
        var token = await UndoTokenAsync(newEmail);
        var other = await CreateUserAsync();
        await FromDatabaseAsync(database => database.Users
            .Where(u => u.Id == other.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Email, ActorUser.User.Email)));

        using var response = await UndoAsync(token, TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal((null, ErrorCodes.Duplicate), (error.Field, error.Code));
        Assert.Equal(newEmail, await EmailOfAsync(ActorId));
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    private async Task<string> UndoTokenAsync(string newEmail, string password = TestUser.Password, int nth = 1)
    {
        var oldEmail = await EmailOfAsync(ActorId);
        using var change = await PostAsync("/api/v1/auth/change-email", new { password, newEmail });
        Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        var confirmToken = (await AwaitEmailAsync(newEmail)).TokenFrom(AppRoutes.ConfirmEmailChange);
        using var confirm = await SendAnonymousAsync(
            HttpMethod.Post, "/api/v1/auth/confirm-email-change", new { token = confirmToken });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        return (await Brevo.AwaitEmailAsync(oldEmail, "EmailChanged", nth)).TokenFrom(AppRoutes.UndoEmailChange);
    }

    private Task<HttpResponseMessage> UndoAsync(string token, string recentPassword, string newPassword) =>
        SendAnonymousAsync(
            HttpMethod.Post, "/api/v1/auth/undo-email-change", new { token, recentPassword, newPassword });

    private Task<HttpResponseMessage> SignInAsync(string password) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password });

    private static async Task AssertWrongPasswordAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("recentPassword", ErrorCodes.WrongPassword), (error.Field, error.Code));
    }

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("token", ErrorCodes.InvalidToken), (error.Field, error.Code));
    }

    private Task<string> EmailOfAsync(Guid userId) =>
        FromDatabaseAsync(database => database.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync());
}
