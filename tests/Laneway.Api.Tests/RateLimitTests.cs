using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class RateLimitTests : ApiTests
{
    private const int ProfileReadsInCreateUser = 1;

    public RateLimitTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task The_11th_sign_in_in_a_minute_from_one_ip_gets_a_429()
    {
        await SendTimesAsync(10, () => SignInAsync());

        using var response = await SignInAsync();

        await AssertTooManyRequestsAsync(response, "10");
    }

    [Fact]
    public async Task The_11th_sign_up_in_an_hour_from_one_ip_gets_a_429()
    {
        await SendTimesAsync(10, () => SignUpAsync(SignUpBody(NewEmail())));

        using var response = await SignUpAsync(SignUpBody(NewEmail()));

        await AssertTooManyRequestsAsync(response, "600");
    }

    [Fact]
    public async Task The_61st_refresh_in_a_minute_from_one_ip_gets_a_429()
    {
        await SendTimesAsync(60, () => RefreshAsync(cookie: null));

        using var response = await RefreshAsync(cookie: null);

        await AssertTooManyRequestsAsync(response, "10");
    }

    [Fact]
    public async Task The_6th_password_check_in_a_minute_by_one_user_gets_a_429()
    {
        await SendTimesAsync(5, () => ChangeEmailAsync(ActorUser));

        using var response = await ChangeEmailAsync(ActorUser);

        await AssertTooManyRequestsAsync(response, "10");
        var limited = Assert.Single(Logs(), log => log.Id.Name == "RateLimited");
        Assert.Equal(AuthLog.Category, limited.Category);
        Assert.Equal(LogLevel.Warning, limited.Level);
        Assert.Equal("password-check", limited.GetStructuredStateValue("policy"));
        Assert.Equal(ActorId.ToString(), limited.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task The_21st_member_add_in_a_minute_by_one_user_gets_a_429_across_both_member_routes()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        object unknown = new { email = "nobody@example.com" };
        await SendTimesAsync(10, () => PostAsync(WorkspaceMembersPath(workspace.Id), unknown));
        await SendTimesAsync(10, () => PostAsync(BoardMembersPath(board.Id), unknown));

        using var response = await PostAsync(WorkspaceMembersPath(workspace.Id), unknown);

        await AssertTooManyRequestsAsync(response, "10");
        Assert.Equal("add-member", Assert.Single(Logs(), log => log.Id.Name == "RateLimited").GetStructuredStateValue("policy"));
    }

    [Fact]
    public async Task The_301st_signed_in_request_in_a_minute_by_one_user_gets_a_429()
    {
        await SendTimesAsync(300 - ProfileReadsInCreateUser, () => GetAsync("/api/v1/profile"));

        using var response = await GetAsync("/api/v1/profile");

        await AssertTooManyRequestsAsync(response, "10");
        var limited = Assert.Single(Logs(), log => log.Id.Name == "RateLimited");
        Assert.Equal("signed-in", limited.GetStructuredStateValue("policy"));
        Assert.Equal(ActorId.ToString(), limited.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task A_password_check_also_counts_toward_the_signed_in_limit()
    {
        await SendTimesAsync(300 - ProfileReadsInCreateUser, () => GetAsync("/api/v1/profile"));

        using var response = await ChangeEmailAsync(ActorUser);

        await AssertTooManyRequestsAsync(response, "10");
        var limited = Assert.Single(Logs(), log => log.Id.Name == "RateLimited");
        Assert.Equal("signed-in", limited.GetStructuredStateValue("policy"));
    }

    [Fact]
    public async Task A_refused_password_check_spends_no_signed_in_permit()
    {
        await SendTimesAsync(5, () => ChangeEmailAsync(ActorUser));
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var refused = await ChangeEmailAsync(ActorUser);
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        }

        await SendTimesAsync(300 - ProfileReadsInCreateUser - 5, () => GetAsync("/api/v1/profile"));
        using var response = await GetAsync("/api/v1/profile");

        await AssertTooManyRequestsAsync(response, "10");
    }

    [Fact]
    public async Task Outside_development_plain_http_counts_toward_the_anonymous_limit()
    {
        var (client, _) = ProductionHost(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        using var first = await client.GetAsync(new Uri("/api/v1/workspaces", UriKind.Relative));
        Assert.Equal(ErrorCodes.HttpsRequired, Assert.Single(await first.ReadErrors()).Code);
        await SendTimesAsync(59, () => client.GetAsync(new Uri("/api/v1/workspaces", UriKind.Relative)));

        using var response = await client.GetAsync(new Uri("/api/v1/workspaces", UriKind.Relative));

        await AssertTooManyRequestsAsync(response, "10");
    }

    [Fact]
    public async Task A_429_to_an_allowed_origin_carries_the_cors_headers()
    {
        await SendTimesAsync(60, () => SendAnonymousAsync(HttpMethod.Get, "/api/v1/workspaces", body: null));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workspaces");
        request.Headers.Add("Origin", ApiFactory.AllowedOrigin);

        using var response = await Https.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task The_61st_anonymous_request_in_a_minute_from_one_ip_gets_a_429()
    {
        await SendTimesAsync(60, () => SendAnonymousAsync(HttpMethod.Get, "/api/v1/workspaces", body: null));

        using var response = await SendAnonymousAsync(HttpMethod.Get, "/api/v1/workspaces", body: null);

        await AssertTooManyRequestsAsync(response, "10");
        var limited = Assert.Single(Logs(), log => log.Id.Name == "RateLimited");
        Assert.Equal("anonymous", limited.GetStructuredStateValue("policy"));
        Assert.Null(limited.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task A_bad_token_counts_as_anonymous()
    {
        await SendTimesAsync(60, () => SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", "not.a.token"));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", "not.a.token");

        await AssertTooManyRequestsAsync(response, "10");
    }

    [Fact]
    public async Task The_auth_routes_do_not_count_toward_the_anonymous_limit()
    {
        await SendTimesAsync(60, () => SendAnonymousAsync(HttpMethod.Get, "/api/v1/workspaces", body: null));

        using var signIn = await SignInAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    [Fact]
    public async Task Two_ips_get_separate_buckets()
    {
        await SendTimesAsync(10, () => SignInAsync("198.51.100.1"));

        using var other = await SignInAsync("198.51.100.2");
        using var same = await SignInAsync("198.51.100.1");

        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, same.StatusCode);
    }

    [Fact]
    public async Task Ipv6_counts_by_its_64_prefix()
    {
        await SendTimesAsync(10, () => SignInAsync("2001:db8:0:1::1"));

        using var samePrefix = await SignInAsync("2001:db8:0:1:ffff::2");
        using var otherPrefix = await SignInAsync("2001:db8:0:2::1");

        Assert.Equal(HttpStatusCode.TooManyRequests, samePrefix.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherPrefix.StatusCode);
    }

    [Fact]
    public async Task An_ipv4_mapped_address_counts_as_its_ipv4_address()
    {
        await SendTimesAsync(10, () => SignInAsync("198.51.100.1"));

        using var response = await SignInAsync("::ffff:198.51.100.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Requests_with_no_ip_share_one_bucket_apart_from_any_ip()
    {
        await SendTimesAsync(10, () => SignInAsync());

        using var withIp = await SignInAsync("198.51.100.1");
        using var withoutIp = await SignInAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, withIp.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, withoutIp.StatusCode);
    }

    [Fact]
    public async Task Two_users_get_separate_buckets()
    {
        var other = await CreateUserAsync();
        await SendTimesAsync(5, () => ChangeEmailAsync(ActorUser));

        using var otherUser = await ChangeEmailAsync(other);
        using var sameUser = await ChangeEmailAsync(ActorUser);

        Assert.Equal(HttpStatusCode.NoContent, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sameUser.StatusCode);
    }

    [Fact]
    public async Task The_meter_counts_rejections_by_policy()
    {
        using var rejections = new MetricCollector<long>(
            Services.GetRequiredService<IMeterFactory>(), AuthMeter.Name, AuthMeter.RateLimitRejectionsName);
        await SendTimesAsync(10, () => SignInAsync());

        (await SignInAsync()).Dispose();
        (await SignInAsync()).Dispose();

        Assert.Equal(
            ["sign-in", "sign-in"],
            rejections.GetMeasurementSnapshot().Select(measurement => measurement.Tags["policy"]));
        Assert.All(Logs().Where(log => log.Id.Name == "RateLimited"), limited =>
        {
            Assert.Equal("sign-in", limited.GetStructuredStateValue("policy"));
            Assert.Null(limited.GetStructuredStateValue("sub"));
        });
    }

    private static async Task SendTimesAsync(int count, Func<Task<HttpResponseMessage>> send)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            using var response = await send();
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private static async Task AssertTooManyRequestsAsync(HttpResponseMessage response, string retryAfter)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(retryAfter, Assert.Single(response.Headers.GetValues("Retry-After")));
        var envelope = await response.ReadEnvelope<object>();
        Assert.False(envelope.Success);
        Assert.Equal(429, envelope.StatusCode);
        var error = Assert.Single(envelope.Errors!);
        Assert.Equal(ErrorCodes.TooManyRequests, error.Code);
        Assert.Null(error.Field);
    }

    private Task<HttpResponseMessage> SignInAsync(string? ip = null)
    {
        var headers = BrowserHeaders();
        if (ip is not null)
        {
            headers[ApiFactory.ClientIpHeader] = ip;
        }

        return SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = "nobody@example.com", password = "not the password" }, headers);
    }

    private Task<HttpResponseMessage> ChangeEmailAsync(TestUser user) =>
        PostAsync("/api/v1/auth/change-email", new { password = TestUser.Password, newEmail = NewEmail() }, user);
}
