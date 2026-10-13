using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SecurityHeaderTests : ApiTests
{
    private const string Proxy = "10.0.0.7";

    private const string Stranger = "203.0.113.9";

    public SecurityHeaderTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_success_carries_the_security_headers()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await GetAsync($"/api/v1/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task A_not_found_carries_the_security_headers()
    {
        using var response = await GetAsync("/api/v1/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task An_unexpected_error_carries_the_security_headers()
    {
        var (client, _) = HostWith(services =>
            services.AddControllers().AddApplicationPart(typeof(FaultController).Assembly));

        using var response = await client.GetAsync(new Uri("/api/v1/faults/unexpected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task The_development_docs_skip_only_the_content_security_policy()
    {
        using var response = await GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Development_takes_plain_http_and_sends_no_hsts()
    {
        using var response = await GetAsync("/api/v1/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public void Kestrel_sends_no_server_header()
    {
        var kestrel = Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.False(kestrel.AddServerHeader);
    }

    [Fact]
    public async Task Outside_development_plain_http_gets_a_400()
    {
        var client = ProductionClient(from: Stranger, baseAddress: "http://localhost");

        using var response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ErrorCodes.HttpsRequired, error.Code);
        Assert.Null(error.Field);
        AssertSecurityHeaders(response);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Outside_development_https_passes_and_sends_hsts()
    {
        var client = ProductionClient(from: Stranger, baseAddress: "https://localhost");

        using var response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("max-age=63072000; includeSubDomains", Header(response, "Strict-Transport-Security"));
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task A_trusted_proxy_that_terminated_tls_counts_as_https()
    {
        var client = ProductionClient(from: Proxy, baseAddress: "http://localhost");
        using var request = ForwardedAsHttps();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("max-age=63072000; includeSubDomains", Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task An_untrusted_caller_cannot_claim_https_with_a_forwarded_header()
    {
        var client = ProductionClient(from: Stranger, baseAddress: "http://localhost");
        using var request = ForwardedAsHttps();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.HttpsRequired, Assert.Single(await response.ReadErrors()).Code);
    }

    [Fact]
    public async Task With_no_proxy_configured_a_forwarded_header_is_ignored()
    {
        var client = ProductionClient(from: Stranger, baseAddress: "http://localhost", trustedProxy: null);
        using var request = ForwardedAsHttps();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.HttpsRequired, Assert.Single(await response.ReadErrors()).Code);
    }

    [Fact]
    public async Task Loopback_is_not_trusted_as_a_proxy_unless_configured()
    {
        var client = ProductionClient(from: "127.0.0.1", baseAddress: "http://localhost");
        using var request = ForwardedAsHttps();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage ForwardedAsHttps()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/no-such-route");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "198.51.100.4");
        return request;
    }

    private HttpClient ProductionClient(string from, string baseAddress, string? trustedProxy = Proxy)
    {
        return ProductionHost(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false
        }, builder =>
        {
            if (trustedProxy is not null)
            {
                builder.UseSetting("ForwardedHeaders:KnownProxies:0", trustedProxy);
            }

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteAddress(IPAddress.Parse(from))));
        }).Client;
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-store", Header(response, "Cache-Control"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(", ", response.Headers.TryGetValues(name, out var values) ? values : []);

    // TestServer leaves the socket address empty, and the forwarded headers rule needs one to check.
    private sealed class RemoteAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
