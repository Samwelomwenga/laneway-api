using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SignUpVerificationTests : ApiTests
{
    public SignUpVerificationTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_new_user_signs_up_then_uses_the_emailed_link_to_sign_in()
    {
        var email = NewEmail();
        using var signUp = await PostOverHttpsAsync("/api/v1/auth/sign-up", SignUpBody(email));

        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        Assert.False(signUp.Headers.Contains("Set-Cookie"));
        var send = await AwaitEmailAsync(email);
        Assert.Equal("Confirm your email for Laneway", send.Subject);
        Assert.Equal(["VerifyEmail"], send.Tags);

        using var verify = await PostOverHttpsAsync(
            "/api/v1/auth/verify-email", new { token = send.TokenFrom(AppRoutes.VerifyEmail) });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        RefreshCookieAssert.Set(verify, Session.AbsoluteLifetime);
        var signedIn = (await verify.ReadEnvelope<SignedInDto>()).Data!;
        using var profile = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/profile", signedIn.AccessToken);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(email, (await profile.ReadEnvelope<UserDto>()).Data!.Email);
    }

    [Fact]
    public async Task Sign_up_with_a_verified_users_email_answers_202_and_sends_nothing()
    {
        var before = await UserByEmailAsync(ActorUser.User.Email);

        using var response = await SignUpAsync(SignUpBody(ActorUser.User.Email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var after = await UserByEmailAsync(ActorUser.User.Email);
        Assert.Equal((before.Username, before.PasswordHash), (after.Username, after.PasswordHash));
        Assert.False(await FromDatabaseAsync(database => database.Deliveries.AnyAsync()));
        Assert.False(await FromDatabaseAsync(database => database.AuthTokens.AnyAsync()));
    }

    [Fact]
    public async Task A_taken_address_answers_exactly_like_a_new_one()
    {
        using var taken = await SignUpAsync(SignUpBody(ActorUser.User.Email));
        using var fresh = await SignUpAsync(SignUpBody(NewEmail()));

        Assert.Equal(fresh.StatusCode, taken.StatusCode);
        Assert.Equal(await fresh.Content.ReadAsStringAsync(), await taken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_taken_address_still_gets_the_password_policy()
    {
        using var taken = await SignUpAsync(SignUpBody(ActorUser.User.Email, "password123"));
        using var fresh = await SignUpAsync(SignUpBody(NewEmail(), "password123"));

        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(
            Assert.Single(await fresh.ReadErrors()).Code,
            Assert.Single(await taken.ReadErrors()).Code);
    }

    [Fact]
    public async Task A_new_sign_up_takes_over_an_unverified_users_address_and_keeps_its_id()
    {
        var email = NewEmail();
        using var squatter = await SignUpAsync(SignUpBody(email, "copper lantern meadow", "squatter"));
        var first = await VerifyTokenSentToAsync(email);
        var squatterRow = await UserByEmailAsync(email);

        using var owner = await SignUpAsync(SignUpBody(email, username: "owner"));

        Assert.Equal(HttpStatusCode.Accepted, owner.StatusCode);
        var sends = await Brevo.AwaitSendsAsync(2);
        var second = sends[1].TokenFrom(AppRoutes.VerifyEmail);
        var ownerRow = await UserByEmailAsync(email);
        Assert.Equal(squatterRow.Id, ownerRow.Id);
        Assert.Equal("owner", ownerRow.Username);
        Assert.False(ownerRow.Verified);
        using var stale = await VerifyEmailAsync(first);
        await AssertInvalidTokenAsync(stale);
        using var verified = await VerifyEmailAsync(second);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var signIn = await SignInAsync(email, SignUpPassword);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task A_sign_up_that_loses_the_race_for_a_new_address_takes_over_the_winner()
    {
        var email = NewEmail();
        var winner = UserRules.Create(
            new NewUser("winner", email, "Wanjiru", "Otieno", null, null, "en-US", "UTC", "Kisumu", null),
            "hash",
            Time.GetUtcNow().UtcDateTime);
        PwnedPasswords.BeforeAnswer = () => FromDatabaseAsync(async database =>
        {
            database.Users.Add(winner);
            return await database.SaveChangesAsync();
        });

        using var response = await SignUpAsync(SignUpBody(email, username: "latest"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var row = await UserByEmailAsync(email);
        Assert.Equal((winner.Id, "latest"), (row.Id, row.Username));
        using var verify = await VerifyEmailAsync(await VerifyTokenSentToAsync(email));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task A_username_held_by_an_unverified_user_is_taken()
    {
        using var first = await SignUpAsync(SignUpBody(NewEmail(), username: "wanjiru"));

        using var response = await SignUpAsync(SignUpBody(NewEmail(), username: "wanjiru"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("username", ErrorCodes.Duplicate), (error.Field, error.Code));
    }

    [Fact]
    public async Task An_unverified_users_username_is_taken_even_with_their_address()
    {
        var email = NewEmail();
        using var first = await SignUpAsync(SignUpBody(email, username: "wanjiru"));

        using var again = await SignUpAsync(SignUpBody(email, username: "wanjiru"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task The_token_is_stored_hashed_and_lives_24_hours()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));

        var token = await VerifyTokenSentToAsync(email);
        Assert.Equal(32, Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length);
        var stored = await FromDatabaseAsync(database => database.AuthTokens.AsNoTracking().SingleAsync());
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), stored.TokenHash);
        Assert.Equal(AuthTokenKind.Verification, stored.Kind);
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddHours(24), stored.ExpiresAt, TimeSpan.FromMilliseconds(1));
        var delivery = await FromDatabaseAsync(database => database.Deliveries.AsNoTracking().SingleAsync());
        Assert.DoesNotContain(token, delivery.Message.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_works_once()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var token = await VerifyTokenSentToAsync(email);
        using var first = await VerifyEmailAsync(token);

        using var second = await VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertInvalidTokenAsync(second);
    }

    [Fact]
    public async Task A_token_past_24_hours_is_invalid()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var token = await VerifyTokenSentToAsync(email);
        Time.Advance(TimeSpan.FromHours(24));

        using var response = await VerifyEmailAsync(token);

        await AssertInvalidTokenAsync(response);
        Assert.False((await UserByEmailAsync(email)).Verified);
    }

    [Fact]
    public async Task An_unknown_token_is_invalid()
    {
        using var response = await VerifyEmailAsync("not-a-real-token");

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task Verifying_clears_the_failure_count_and_the_sign_in_block()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var token = await VerifyTokenSentToAsync(email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await SignInAsync(email, "wrong plum tractor");
        }

        Assert.NotNull((await UserByEmailAsync(email)).SignInBlockedUntil);

        using var response = await VerifyEmailAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await UserByEmailAsync(email);
        Assert.True(user.Verified);
        Assert.Equal(0, user.FailedSignInCount);
        Assert.Null(user.SignInBlockedUntil);
        using var signIn = await SignInAsync(email, SignUpPassword);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task An_unverified_user_with_the_right_password_gets_403_and_no_session()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));

        using var response = await SignInAsync(email, SignUpPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.EmailNotVerified, Assert.Single(await response.ReadErrors()).Code);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var userId = (await UserByEmailAsync(email)).Id;
        Assert.False(await FromDatabaseAsync(database => database.Sessions.AnyAsync(s => s.UserId == userId)));
    }

    [Fact]
    public async Task An_unverified_user_with_a_wrong_password_gets_401_and_a_counted_failure()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));

        using var response = await SignInAsync(email, "wrong plum tractor");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, (await UserByEmailAsync(email)).FailedSignInCount);
    }

    [Fact]
    public async Task Member_lookup_finds_verified_users_only()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email, username: "unverified"));
        var userId = (await UserByEmailAsync(email)).Id;
        var workspace = await CreateWorkspaceAsync("Design");

        foreach (var (field, body) in new (string, object)[]
        {
            ("userId", new { userId }),
            ("username", new { username = "unverified" }),
            ("email", new { email })
        })
        {
            using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Equal((field, ErrorCodes.NotFound), (error.Field, error.Code));
        }
    }

    [Fact]
    public async Task The_migration_marks_every_existing_user_verified()
    {
        var existing = Guid.NewGuid();

        await MigrateWithUsersAsync("VerifyEmail", (existing, "kamau", Time.GetUtcNow().UtcDateTime));

        Assert.True(await FromDatabaseAsync(database =>
            database.Users.Where(u => u.Id == existing).Select(u => u.Verified).SingleAsync()));
    }

    private Task<User> UserByEmailAsync(string email) =>
        FromDatabaseAsync(database => database.Users.AsNoTracking().SingleAsync(u => u.Email == email));

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email, password });

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(("token", ErrorCodes.InvalidToken), (error.Field, error.Code));
    }
}
