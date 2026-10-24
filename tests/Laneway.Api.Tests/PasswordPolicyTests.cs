using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class PasswordPolicyTests : ApiTests
{
    public PasswordPolicyTests(TestStack stack) : base(stack)
    {
    }

    [Theory]
    [InlineData("password123")]
    [InlineData("PassWord123")]
    [InlineData("qwertyuiop")]
    public async Task Sign_up_refuses_a_common_password_in_any_case(string password)
    {
        using var response = await SignUpAsync(SignUpBody("ana@example.com", password));

        await AssertRefusedAsync(response, ErrorCodes.CommonPassword, "common");
        Assert.Empty(PwnedPasswords.Requests);
    }

    [Theory]
    [InlineData("tractorfan", "bigtractorfan@example.com", "my TRACTORFAN pass")]
    [InlineData("ana-waweru", "plumcake@example.com", "eat more PlumCake today")]
    [InlineData("ana-waweru", "ana@example.com", "my laneway board 7")]
    [InlineData("ana-waweru", "ana@example.com", "my LaneWay board 7")]
    public async Task Sign_up_refuses_a_password_containing_a_context_word(string username, string email, string password)
    {
        using var response = await SignUpAsync(SignUpBody(email, password, username));

        await AssertRefusedAsync(response, ErrorCodes.ContainsContextWord, "username");
        Assert.Empty(PwnedPasswords.Requests);
    }

    [Theory]
    [InlineData("bob", "jo@example.com", "bob and jo plant trees")]
    [InlineData("ana-waweru", "🔑🔑🔑@example.com", "🔑🔑🔑 and plum trees")]
    public async Task Context_words_under_4_code_points_are_ignored(string username, string email, string password)
    {
        using var response = await SignUpAsync(SignUpBody(email, password, username));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_context_word_of_4_code_points_counts()
    {
        using var response = await SignUpAsync(SignUpBody("🔑🔑🔑🔑@example.com", "my 🔑🔑🔑🔑 and plum trees", "ana-waweru"));

        await AssertRefusedAsync(response, ErrorCodes.ContainsContextWord, "username");
    }

    [Fact]
    public async Task Sign_up_refuses_a_breached_password_and_sends_only_a_padded_5_character_prefix()
    {
        const string password = "violet harbor kettle";
        PwnedPasswords.Breach(password);

        using var response = await SignUpAsync(SignUpBody("ana@example.com", password));

        await AssertRefusedAsync(response, ErrorCodes.BreachedPassword, "breach");
        var request = Assert.Single(PwnedPasswords.Requests);
        Assert.Equal(
            new Uri($"https://api.pwnedpasswords.com/range/{PwnedPasswordsStub.Sha1(password)[..5]}"),
            request.Uri);
        Assert.Equal("true", request.Padding);
    }

    [Fact]
    public async Task The_breached_check_hashes_the_nfkc_password()
    {
        PwnedPasswords.Breach("five plum tractors");

        using var response = await SignUpAsync(SignUpBody("ana@example.com", "ﬁve plum tractors"));

        await AssertRefusedAsync(response, ErrorCodes.BreachedPassword, "breach");
    }

    [Fact]
    public async Task A_password_that_passes_every_check_signs_up()
    {
        PwnedPasswords.Breach("some other password");

        using var response = await SignUpAsync(SignUpBody("ana@example.com", "violet harbor kettle"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(PwnedPasswords.Requests);
    }

    [Fact]
    public async Task The_checks_stop_at_the_first_failure_in_order()
    {
        PwnedPasswords.Breach("password123");
        PwnedPasswords.Breach("laneway plum tractor");

        using var common = await SignUpAsync(SignUpBody("password@example.com", "password123"));
        using var context = await SignUpAsync(SignUpBody("ana@example.com", "laneway plum tractor"));

        Assert.Equal(ErrorCodes.CommonPassword, Assert.Single((await common.ReadEnvelope<object>()).Errors!).Code);
        Assert.Equal(ErrorCodes.ContainsContextWord, Assert.Single((await context.ReadEnvelope<object>()).Errors!).Code);
        Assert.Empty(PwnedPasswords.Requests);
    }

    [Fact]
    public async Task Sign_in_never_runs_the_breached_check()
    {
        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = TestUser.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(PwnedPasswords.Requests);
    }

    [Fact]
    public async Task A_too_short_password_never_reaches_the_policy()
    {
        using var response = await SignUpAsync(SignUpBody("ana@example.com", "laneway"));

        Assert.Equal(ErrorCodes.TooShort, Assert.Single((await response.ReadEnvelope<object>()).Errors!).Code);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("timeout")]
    public async Task An_unavailable_breached_check_accepts_the_password_and_warns(string failure)
    {
        using var unavailable = new MetricCollector<long>(
            Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            AuthMeter.Name,
            AuthMeter.BreachedCheckUnavailableName);
        PwnedPasswords.Breach("violet harbor kettle");
        PwnedPasswords.Failure = failure == "timeout"
            ? new TaskCanceledException("timed out", new TimeoutException())
            : new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable);

        using var response = await SignUpAsync(SignUpBody("ana@example.com", "violet harbor kettle"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var logged = Assert.Single(Logs(), log => log.Id.Name == "BreachedCheckUnavailable");
        Assert.Equal(AuthLog.Category, logged.Category);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Same(PwnedPasswords.Failure, logged.Exception);
        Assert.Equal(1, Assert.Single(unavailable.GetMeasurementSnapshot()).Value);
    }

    [Fact]
    public async Task No_log_carries_the_hash_prefix()
    {
        const string password = "violet harbor kettle";
        var prefix = PwnedPasswordsStub.Sha1(password)[..5];

        using var response = await SignUpAsync(SignUpBody("ana@example.com", password));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(Logs(), log =>
            log.Message.Contains(prefix, StringComparison.OrdinalIgnoreCase)
            || (log.StructuredState ?? []).Any(pair =>
                pair.Value?.Contains(prefix, StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public void The_breached_check_client_waits_at_most_2_seconds()
    {
        using var client = Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BreachedPasswords));

        Assert.Equal(TimeSpan.FromSeconds(2), client.Timeout);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code, string messagePart)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.ReadEnvelope<object>();
        Assert.Equal("Invalid data", body.Message);
        var error = Assert.Single(body.Errors!);
        Assert.Equal("password", error.Field);
        Assert.Equal(code, error.Code);
        Assert.Contains(messagePart, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
