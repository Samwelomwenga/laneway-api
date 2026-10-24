using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CsrfTests : ApiTests
{
    public static TheoryData<string> CookieRoutes =>
        ["/api/v1/auth/sign-up", "/api/v1/auth/sign-in", "/api/v1/auth/refresh", "/api/v1/auth/verify-email"];

    public static TheoryData<string> BodyRoutes =>
        ["/api/v1/auth/sign-up", "/api/v1/auth/sign-in", "/api/v1/auth/verify-email"];

    public CsrfTests(TestStack stack) : base(stack)
    {
    }

    [Theory]
    [MemberData(nameof(CookieRoutes))]
    public async Task A_missing_csrf_header_gets_403(string path)
    {
        var headers = BrowserHeaders();
        headers.Remove(CsrfCheck.Header);

        using var response = await SendAsync(path, headers);

        await AssertRejectedAsync(response, "header");
    }

    [Theory]
    [MemberData(nameof(CookieRoutes))]
    public async Task A_csrf_header_other_than_1_gets_403(string path)
    {
        var headers = BrowserHeaders();
        headers[CsrfCheck.Header] = "true";

        using var response = await SendAsync(path, headers);

        await AssertRejectedAsync(response, "header");
    }

    [Theory]
    [MemberData(nameof(CookieRoutes))]
    public async Task An_origin_off_the_list_gets_403(string path)
    {
        var headers = BrowserHeaders();
        headers["Origin"] = "https://evil.example";

        using var response = await SendAsync(path, headers);

        await AssertRejectedAsync(response, "origin");
    }

    [Theory]
    [MemberData(nameof(CookieRoutes))]
    public async Task A_cross_site_fetch_gets_403(string path)
    {
        var headers = BrowserHeaders();
        headers["Sec-Fetch-Site"] = "cross-site";

        using var response = await SendAsync(path, headers);

        await AssertRejectedAsync(response, "fetch-site");
    }

    [Fact]
    public async Task A_missing_origin_passes()
    {
        var headers = BrowserHeaders();
        headers.Remove("Origin");

        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = TestUser.Password }, headers);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_apis_own_origin_passes()
    {
        var headers = BrowserHeaders();
        headers["Origin"] = "http://localhost";
        headers["Sec-Fetch-Site"] = "same-origin";

        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = TestUser.Password }, headers);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(CookieRoutes))]
    public async Task Another_port_on_the_apis_host_gets_403(string path)
    {
        var headers = BrowserHeaders();
        headers["Origin"] = "http://localhost:3001";

        using var response = await SendAsync(path, headers);

        await AssertRejectedAsync(response, "origin");
    }

    [Theory]
    [InlineData("same-origin")]
    [InlineData("none")]
    public async Task A_same_origin_or_user_started_fetch_passes(string fetchSite)
    {
        var headers = BrowserHeaders();
        headers["Sec-Fetch-Site"] = fetchSite;

        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = TestUser.Password }, headers);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_check_returns_the_same_body()
    {
        var bodies = new List<string>();
        foreach (var headers in OneFailurePerCheck())
        {
            using var response = await SendAsync("/api/v1/auth/sign-in", headers);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_rejection_stops_before_the_endpoint_runs()
    {
        var headers = BrowserHeaders();
        headers.Remove(CsrfCheck.Header);

        using var response = await SendFromBrowserAsync("/api/v1/auth/sign-up", SignUpBody(NewEmail()), headers);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SignUp");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_rejections_counter_counts_each_check()
    {
        using var rejections = new MetricCollector<long>(
            Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            AuthMeter.Name,
            AuthMeter.CsrfRejectionsName);
        foreach (var headers in OneFailurePerCheck())
        {
            (await SendAsync("/api/v1/auth/sign-in", headers)).Dispose();
        }

        Assert.Equal(
            ["header", "origin", "fetch_site"],
            rejections.GetMeasurementSnapshot().Select(m => m.Tags["check"]?.ToString()));
        Assert.All(rejections.GetMeasurementSnapshot(), m => Assert.Equal(1, m.Value));
    }

    [Fact]
    public async Task A_bearer_endpoint_needs_no_csrf_header()
    {
        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, "/api/v1/auth/sign-out", ActorUser.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BodyRoutes))]
    public async Task A_form_encoded_body_gets_415(string path)
    {
        using var response = await SendFromBrowserAsync(
            path, new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["email"] = ActorUser.User.Email,
                ["password"] = TestUser.Password
            }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BodyRoutes))]
    public async Task A_multipart_body_gets_415(string path)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(ActorUser.User.Email), "email" },
            { new StringContent(TestUser.Password), "password" }
        };

        using var response = await SendFromBrowserAsync(path, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BodyRoutes))]
    public async Task A_text_plain_body_gets_415(string path)
    {
        using var response = await SendFromBrowserAsync(
            path,
            new StringContent(
                $$"""{"email":"{{ActorUser.User.Email}}","password":"{{TestUser.Password}}"}""",
                Encoding.UTF8,
                new MediaTypeHeaderValue("text/plain")));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private static IEnumerable<Dictionary<string, string>> OneFailurePerCheck()
    {
        var noHeader = BrowserHeaders();
        noHeader.Remove(CsrfCheck.Header);
        var badOrigin = BrowserHeaders();
        badOrigin["Origin"] = "https://evil.example";
        var crossSite = BrowserHeaders();
        crossSite["Sec-Fetch-Site"] = "cross-site";
        return [noHeader, badOrigin, crossSite];
    }

    private Task<HttpResponseMessage> SendAsync(string path, IReadOnlyDictionary<string, string> headers) =>
        SendFromBrowserAsync(
            path,
            path.EndsWith("/refresh", StringComparison.Ordinal)
                ? null
                : new { email = ActorUser.User.Email, password = TestUser.Password },
            headers);

    private async Task AssertRejectedAsync(HttpResponseMessage response, string check)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var envelope = await response.ReadEnvelope<object>();
        Assert.False(envelope.Success);
        Assert.Null(envelope.Data);
        Assert.Equal(403, envelope.StatusCode);
        var error = Assert.Single(envelope.Errors!);
        Assert.Equal(ErrorCodes.CsrfRejected, error.Code);
        Assert.Null(error.Field);

        var logged = Assert.Single(Logs(), log => log.Id.Name == "CsrfRejected");
        Assert.Equal(AuthLog.Category, logged.Category);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(check, logged.GetStructuredStateValue("check"));
    }
}
