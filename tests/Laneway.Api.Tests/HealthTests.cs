using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class HealthTests : ApiTests
{
    private static readonly Action<IWebHostBuilder> UnreachablePostgres = builder => builder.UseSetting(
        "ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=none");

    public HealthTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Live_answers_healthy_to_an_anonymous_caller_over_plain_http()
    {
        var client = PlainHttpClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_answers_healthy_when_postgres_responds()
    {
        var client = PlainHttpClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_answers_unhealthy_when_postgres_is_unreachable()
    {
        var client = PlainHttpClient(UnreachablePostgres);

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Live_stays_healthy_when_postgres_is_unreachable()
    {
        var client = PlainHttpClient(UnreachablePostgres);

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_passing_probe_logs_its_request_line_at_verbose()
    {
        var (client, logs) = PlainHttpHost(builder => builder.UseSetting(
            "Serilog:MinimumLevel:Override:Serilog.AspNetCore.RequestLoggingMiddleware", "Verbose"));

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single(logs.GetSnapshot(), log => log.Id.Name == RequestLine.EventName);
        Assert.Equal(LogLevel.Trace, line.Level);
        Assert.Equal("200", line.GetStructuredStateValue("StatusCode"));
    }

    [Fact]
    public async Task A_failing_probe_logs_its_request_line_at_error()
    {
        var (client, logs) = PlainHttpHost(UnreachablePostgres);

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var line = Assert.Single(logs.GetSnapshot(), log => log.Id.Name == RequestLine.EventName);
        Assert.Equal(LogLevel.Error, line.Level);
    }

    [Fact]
    public async Task A_passing_probe_ships_no_spans()
    {
        var client = PlainHttpClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ShippedSpans, span => span.GetTagItem("http.route") as string == "/health/ready");
        Assert.DoesNotContain(ShippedSpans, span => span.TagObjects.Any(tag => tag.Value as string == "SELECT 1;"));
    }

    [Fact]
    public async Task A_failing_probe_ships_its_server_span_as_an_error()
    {
        var client = PlainHttpClient(UnreachablePostgres);

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var server = ServerSpan("/health/ready");
        Assert.Equal(ActivityStatusCode.Error, server.Status);
    }

    private (HttpClient Client, FakeLogCollector Logs) PlainHttpHost(Action<IWebHostBuilder>? configure = null)
    {
        var (client, services) = ProductionHost(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") }, configure);
        return (client, services.GetFakeLogCollector());
    }

    private HttpClient PlainHttpClient(Action<IWebHostBuilder>? configure = null) => PlainHttpHost(configure).Client;
}
