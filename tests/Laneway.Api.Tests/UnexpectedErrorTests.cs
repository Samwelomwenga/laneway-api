using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class UnexpectedErrorTests : ApiTests
{
    private const string ExceptionHandlerMiddleware = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    public UnexpectedErrorTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task An_unexpected_error_gets_a_generic_500_and_one_error_log_with_the_same_trace_id()
    {
        var (client, collector) = WithFaults();

        using var response = await client.GetAsync(new Uri("/api/v1/faults/unexpected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FaultController.Detail, raw, StringComparison.Ordinal);
        var body = await response.ReadEnvelope<object>();
        Assert.Equal("An unexpected error occurred", body.Message);
        Assert.False(string.IsNullOrEmpty(body.TraceId));

        var error = Assert.Single(ErrorLogs(collector));
        Assert.Equal(body.TraceId, error.GetStructuredStateValue("traceId"));
        Assert.Equal(FaultController.Detail, Assert.IsType<InvalidOperationException>(error.Exception).Message);
    }

    [Fact]
    public async Task The_exception_handler_middleware_stays_muted()
    {
        var (client, collector) = WithFaults();

        using var response = await client.GetAsync(new Uri("/api/v1/faults/unexpected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(collector.GetSnapshot(), log => log.Category == ExceptionHandlerMiddleware);
        Assert.DoesNotContain(ShippedLogs, log => log.CategoryName == ExceptionHandlerMiddleware);
    }

    [Theory]
    [InlineData("foreign-key", HttpStatusCode.BadRequest)]
    [InlineData("unique", HttpStatusCode.Conflict)]
    public async Task An_expected_database_error_is_not_logged(string fault, HttpStatusCode status)
    {
        var (client, collector) = WithFaults();

        using var response = await client.GetAsync(new Uri($"/api/v1/faults/{fault}", UriKind.Relative));

        Assert.Equal(status, response.StatusCode);
        Assert.Null((await response.ReadEnvelope<object>()).TraceId);
        Assert.DoesNotContain(collector.GetSnapshot(), log => log.Category == typeof(ApiExceptionHandler).FullName);
        Assert.Empty(ErrorLogs(collector));
    }

    private (HttpClient Client, FakeLogCollector Collector) WithFaults() =>
        HostWith(services => services.AddControllers().AddApplicationPart(typeof(FaultController).Assembly));

    private static IEnumerable<FakeLogRecord> ErrorLogs(FakeLogCollector collector) =>
        collector.GetSnapshot().Where(log => log.Level >= LogLevel.Error);
}

[ApiController]
[Route("api/v1/faults")]
[AllowAnonymous]
public sealed class FaultController : ControllerBase
{
    public const string Detail = "connection to db-7 refused";

    [HttpGet("{fault}")]
    public IActionResult Throw(string fault) => throw (fault switch
    {
        "foreign-key" => DatabaseError(PostgresErrorCodes.ForeignKeyViolation),
        "unique" => DatabaseError(PostgresErrorCodes.UniqueViolation),
        _ => new InvalidOperationException(Detail)
    });

    private static DbUpdateException DatabaseError(string sqlState) =>
        new("save failed", new PostgresException(Detail, "ERROR", "ERROR", sqlState));
}
