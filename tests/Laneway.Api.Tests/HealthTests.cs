using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private HttpClient PlainHttpClient(Action<IWebHostBuilder>? configure = null) =>
        ProductionHost(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") }, configure).Client;
}
