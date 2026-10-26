using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class RequestLineTests : ApiTests
{
    private const string Ip = "203.0.113.7";

    public RequestLineTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task An_unmatched_path_logs_its_status_and_never_the_path()
    {
        using var response = await SendFromAsync(
            Ip, HttpMethod.Get, "/api/v1/nowhere/canary-segment?q=canary-query", ActorUser);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue(ClientIp.Field) == Ip);
        Assert.Equal(RequestLine.Category, line.Category);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("GET", line.GetStructuredStateValue("RequestMethod"));
        Assert.Equal(RequestLine.Unmatched, line.GetStructuredStateValue("route"));
        Assert.Equal("404", line.GetStructuredStateValue("StatusCode"));
        Assert.NotNull(line.GetStructuredStateValue("Elapsed"));
        Assert.Equal(Ip, line.GetStructuredStateValue(ClientIp.Field));
        Assert.DoesNotContain(Logs(), log => log.Message.Contains("canary", StringComparison.Ordinal)
            || log.StructuredState!.Any(pair => pair.Value?.Contains("canary", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task A_signed_in_request_logs_its_route_template_and_session()
    {
        var workspace = await CreateWorkspaceAsync("Finance");

        using var response = await SendFromAsync(Ip, HttpMethod.Get, $"/api/v1/workspaces/{workspace.Id}", ActorUser);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue(ClientIp.Field) == Ip);
        Assert.Equal("api/v1/WorkSpaces/{id}", line.GetStructuredStateValue("route"));
        Assert.Equal("200", line.GetStructuredStateValue("StatusCode"));
        Assert.Equal(ActorId.ToString(), line.GetStructuredStateValue("sub"));
        Assert.Equal(ActorUser.SessionId.ToString(), line.GetStructuredStateValue("sid"));
        Assert.DoesNotContain(workspace.Id.ToString(), line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_4xx_logs_at_information()
    {
        using var response = await SendFromAsync(
            Ip, HttpMethod.Post, "/api/v1/workspaces", ActorUser, new { name = "", visibility = "Public" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue(ClientIp.Field) == Ip);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("POST", line.GetStructuredStateValue("RequestMethod"));
        Assert.Equal("400", line.GetStructuredStateValue("StatusCode"));
    }

    [Fact]
    public async Task A_5xx_logs_at_error_with_its_route_and_the_error_has_no_address()
    {
        var (client, collector) = HostWith(services =>
            services.AddControllers().AddApplicationPart(typeof(FaultController).Assembly));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/faults/unexpected");
        request.Headers.Add(ApiFactory.ClientIpHeader, Ip);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var line = Assert.Single(collector.GetSnapshot(), log => log.Id.Name == RequestLine.EventName);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("api/v1/faults/{fault}", line.GetStructuredStateValue("route"));
        Assert.Equal("500", line.GetStructuredStateValue("StatusCode"));
        Assert.Equal(Ip, line.GetStructuredStateValue(ClientIp.Field));
        var error = Assert.Single(collector.GetSnapshot(), log => log.Id.Name == "UnexpectedError");
        Assert.Null(error.GetStructuredStateValue(ClientIp.Field));
    }

    [Fact]
    public async Task A_request_over_a_second_logs_at_warning()
    {
        PwnedPasswords.BeforeAnswer = () => Task.Delay(TimeSpan.FromMilliseconds(1100));

        var headers = BrowserHeaders();
        headers[ApiFactory.ClientIpHeader] = Ip;

        using var response = await SendFromBrowserAsync("/api/v1/auth/sign-up", SignUpBody(NewEmail()), headers);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue(ClientIp.Field) == Ip);
        Assert.Equal(LogLevel.Warning, line.Level);
    }

    [Fact]
    public async Task An_unknown_method_logs_as_other()
    {
        using var response = await SendFromAsync(Ip, new HttpMethod("PROPFIND"), "/api/v1/workspaces", ActorUser);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue(ClientIp.Field) == Ip);
        Assert.Equal(RequestLine.OtherMethod, line.GetStructuredStateValue("RequestMethod"));
    }

    [Fact]
    public async Task A_request_with_no_remote_address_leaves_the_address_off()
    {
        using var response = await GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = await RequestLineAsync(log => log.GetStructuredStateValue("route") == "api/v1/WorkSpaces");
        Assert.Null(line.GetStructuredStateValue(ClientIp.Field));
    }

    [Fact]
    public async Task Auth_events_carry_the_address()
    {
        var headers = BrowserHeaders();
        headers[ApiFactory.ClientIpHeader] = Ip;
        var noCsrf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ApiFactory.ClientIpHeader] = Ip };
        var body = new { email = "nobody@example.com", password = "not the password" };

        using var failed = await SendFromBrowserAsync("/api/v1/auth/sign-in", body, headers);
        using var rejected = await SendFromBrowserAsync("/api/v1/auth/sign-in", body, noCsrf);

        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(Ip, Assert.Single(Logs(), log => log.Id.Name == "SignInFailed").GetStructuredStateValue(ClientIp.Field));
        Assert.Equal(Ip, Assert.Single(Logs(), log => log.Id.Name == "CsrfRejected").GetStructuredStateValue(ClientIp.Field));
    }

    private async Task<FakeLogRecord> RequestLineAsync(Func<FakeLogRecord, bool> matches)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (Logs().LastOrDefault(log => log.Id.Name == RequestLine.EventName && matches(log)) is { } line)
            {
                return line;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("No matching request line logged within 5 seconds.");
    }
}
