using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CorsTests : ApiTests
{
    public CorsTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_preflight_from_an_allowed_origin_is_answered()
    {
        using var response = await PreflightAsync(ApiFactory.AllowedOrigin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
        Assert.Equal(
            ["DELETE", "GET", "POST", "PUT"],
            Values(response, "Access-Control-Allow-Methods").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["authorization", "content-type", "x-laneway-csrf"],
            Values(response, "Access-Control-Allow-Headers")
                .Select(name => name.ToLowerInvariant())
                .Order(StringComparer.Ordinal));
        Assert.Equal("7200", Header(response, "Access-Control-Max-Age"));
    }

    [Fact]
    public async Task A_preflight_to_a_bearer_route_needs_no_token()
    {
        using var response = await PreflightAsync(ApiFactory.AllowedOrigin, "/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_preflight_from_another_origin_gets_no_cors_headers()
    {
        using var response = await PreflightAsync("https://evil.example");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task A_preflight_asking_for_patch_gets_no_allowed_methods()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/workspaces");
        request.Headers.Add("Origin", ApiFactory.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "PATCH");

        using var response = await Https.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("PATCH", Values(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task A_request_from_an_allowed_origin_exposes_location_and_retry_after()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workspaces");
        request.Headers.Add("Origin", ApiFactory.AllowedOrigin);
        request.Headers.Authorization = new("Bearer", ActorUser.AccessToken);

        using var response = await Https.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal(
            ["location", "retry-after"],
            Values(response, "Access-Control-Expose-Headers")
                .Select(name => name.ToLowerInvariant())
                .Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.laneway.example")]
    [InlineData("null")]
    [InlineData("https://app.laneway.example/")]
    [InlineData("https://app.laneway.example/app")]
    [InlineData("http://app.laneway.example")]
    [InlineData("app.laneway.example")]
    [InlineData("https://app.laneway.example?x=1")]
    public void A_bad_allowed_origin_stops_the_app_at_startup(string origin)
    {
        using var factory = new ApiFactory("Host=localhost;Database=never_opened");
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Cors:AllowedOrigins:0"] = origin
            })));

        var thrown = Assert.Throws<OptionsValidationException>(() => broken.CreateClient());

        Assert.Contains(origin, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_origin_list_is_allowed()
    {
        var result = new CorsOriginsValidator().Validate(null, new CorsOrigins());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_port_is_allowed()
    {
        var result = new CorsOriginsValidator().Validate(
            null, new CorsOrigins { AllowedOrigins = ["https://localhost:5173"] });

        Assert.True(result.Succeeded);
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin, string path = "/api/v1/auth/sign-in")
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", $"authorization, content-type, {CsrfCheck.Header}");
        return await Https.SendAsync(request);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(", ", Values(response, name));

    private static List<string> Values(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries)).ToList()
            : [];
}
