using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
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
    public async Task A_log_inside_a_request_ships_without_its_path_or_request_id()
    {
        using var response = await SignInWithTheWrongPasswordAsync();

        var shipped = Assert.Single(ShippedLogs, log => log.EventId.Name == "SignInFailed");
        var keys = shipped.Attributes!.Select(pair => pair.Key).ToList();
        Assert.Contains("ActionName", keys);
        Assert.DoesNotContain("RequestPath", keys);
        Assert.DoesNotContain("RequestId", keys);
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

    [Fact]
    public async Task A_server_span_ships_without_the_user_agent()
    {
        using var response = await SignInWithTheWrongPasswordAsync(userAgent: "Mozilla/5.0 Canary/1.0");

        var server = Assert.Single(ShippedSpans, span =>
            span.Kind == ActivityKind.Server && span.GetTagItem("http.route") as string == "api/v1/auth/sign-in");
        Assert.Null(server.GetTagItem("user_agent.original"));
    }

    [Fact]
    public async Task A_server_span_ships_its_route_without_the_raw_path_or_query()
    {
        var id = Guid.NewGuid();

        using var response = await GetAsync($"/api/v1/boards/{id}?canary=planted");

        var server = Assert.Single(ShippedSpans, span =>
            span.Kind == ActivityKind.Server
            && string.Equals(span.GetTagItem("http.route") as string, "api/v1/boards/{id}", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.TagObjects, tag => tag.Value is string text
            && (text.Contains(id.ToString(), StringComparison.Ordinal) || text.Contains("planted", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_database_span_ships_without_the_connection_string()
    {
        using var response = await SignInWithTheWrongPasswordAsync();

        var database = ShippedSpans.Where(span => span.Source.Name == "Npgsql").ToList();
        Assert.NotEmpty(database);
        Assert.All(database, span => Assert.Null(span.GetTagItem("db.connection_string")));
    }

    [Fact]
    public async Task The_pwned_passwords_span_ships_its_url_as_a_template()
    {
        using var response = await SignUpAsync(SignUpBody(NewEmail()));

        var pwned = Assert.Single(ShippedSpans, span => span.GetTagItem("server.address") as string == "api.pwnedpasswords.com");
        Assert.Equal("https://api.pwnedpasswords.com/range/{prefix}", pwned.GetTagItem("url.full"));
        Assert.True(pwned.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task A_failed_database_command_ships_its_error_type_without_row_values()
    {
        var other = await CreateUserAsync();
        PwnedPasswords.BeforeAnswer = () => FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == other.Id)
            .ExecuteUpdateAsync(user => user.SetProperty(u => u.Username, "wanjiru")));

        using var response = await SignUpAsync(SignUpBody(NewEmail(), username: "wanjiru"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var failed = Assert.Single(ShippedSpans, span =>
            span.Source.Name == "Npgsql" && span.Events.Any(recorded => recorded.Name == "exception"));
        Assert.Equal("Npgsql.PostgresException", failed.GetTagItem("error.type"));
        Assert.Equal("ERROR", failed.GetTagItem("otel.status_code"));
        Assert.Null(failed.GetTagItem("otel.status_description"));
        Assert.Null(failed.StatusDescription);
        Assert.All(failed.Events, recorded => Assert.DoesNotContain(
            recorded.Tags,
            tag => tag.Value is string text && text.Contains("wanjiru", StringComparison.Ordinal)));
    }

    private Task<HttpResponseMessage> SignInWithTheWrongPasswordAsync(string? userAgent = null) =>
        SendFromBrowserAsync(
            "/api/v1/auth/sign-in",
            new { email = ActorUser.User.Email, password = "not the password" },
            userAgent: userAgent);
}
