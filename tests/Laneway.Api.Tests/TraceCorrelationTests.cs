using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class TraceCorrelationTests : ApiTests
{
    public TraceCorrelationTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_problem_details_body_quotes_the_trace_id_of_its_request()
    {
        var (client, _) = HostWith(services => services.AddControllers().AddApplicationPart(typeof(FaultController).Assembly));

        using var response = await client.GetAsync(new Uri("/api/v1/faults/problem", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ServerSpan("api/v1/faults/problem").TraceId.ToHexString(), problem!.Extensions["traceId"]?.ToString());
    }

    [Fact]
    public async Task An_inbound_traceparent_starts_no_trace_of_its_own()
    {
        const string planted = "4bf92f3577b34da6a3ce929d0e0e4736";
        var headers = BrowserHeaders();
        headers["traceparent"] = $"00-{planted}-00f067aa0ba902b7-01";
        headers["tracestate"] = "planted=1";

        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = "not the password" }, headers);

        var server = ServerSpan("api/v1/auth/sign-in");
        Assert.NotEqual(planted, server.TraceId.ToHexString());
        Assert.Equal(default, server.ParentSpanId);
        Assert.Null(server.TraceStateString);
    }

    [Fact]
    public async Task Pwned_passwords_gets_no_trace_context_and_its_span_still_ships()
    {
        using var response = await SignUpAsync(SignUpBody(NewEmail()));

        var wire = Assert.Single(PwnedPasswords.WireHeaderNames);
        Assert.Contains("Add-Padding", wire, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("traceparent", wire, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracestate", wire, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("baggage", wire, StringComparer.OrdinalIgnoreCase);
        var pwned = Assert.Single(ShippedSpans, span => span.GetTagItem("server.address") as string == "api.pwnedpasswords.com");
        Assert.Equal(ServerSpan("api/v1/auth/sign-up").TraceId, pwned.TraceId);
    }
}
