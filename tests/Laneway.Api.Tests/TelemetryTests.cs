using System.Diagnostics;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class TelemetryTests : ApiTests
{
    public TelemetryTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_log_ships_under_its_category_with_its_event_name()
    {
        using var response = await SignInWithTheWrongPasswordAsync();

        var shipped = Assert.Single(ShippedLogs, log => log.EventId.Name == "SignInFailed");
        Assert.Equal(AuthLog.Category, shipped.CategoryName);
        Assert.Contains(shipped.Attributes!, pair => pair.Key == "sub" && pair.Value?.ToString() == ActorId.ToString());
    }

    [Fact]
    public async Task A_request_ships_a_server_span_and_its_database_spans()
    {
        using var response = await SignInWithTheWrongPasswordAsync();

        var server = Assert.Single(ShippedSpans, span =>
            span.Kind == ActivityKind.Server && span.GetTagItem("http.route") as string == "api/v1/auth/sign-in");
        Assert.Contains(ShippedSpans, span => span.Source.Name == "Npgsql" && span.TraceId == server.TraceId);
    }

    [Fact]
    public async Task The_laneway_counters_ship()
    {
        using var response = await SignInWithTheWrongPasswordAsync();

        Assert.Contains(ShippedMetrics(), metric =>
            metric.MeterName == AuthMeter.Name && metric.Name == AuthMeter.SignInAttemptsName);
    }

    private Task<HttpResponseMessage> SignInWithTheWrongPasswordAsync() =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = "not the password" });
}
