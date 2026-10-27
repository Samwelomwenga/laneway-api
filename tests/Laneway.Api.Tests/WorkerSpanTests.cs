using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class WorkerSpanTests : ApiTests
{
    public WorkerSpanTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_delivery_attempt_links_to_the_request_that_queued_it()
    {
        var email = NewEmail();
        using var response = await SignUpAsync(SignUpBody(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var attempt = Assert.Single(await AwaitSpansAsync(WorkerSpans.MessageSource, 1));
        var request = ServerSpan("api/v1/auth/sign-up");
        var link = Assert.Single(attempt.Links);
        Assert.Equal(request.TraceId, link.Context.TraceId);
        Assert.Equal(request.SpanId, link.Context.SpanId);
        Assert.NotEqual(request.TraceId, attempt.TraceId);
        Assert.Equal(default, attempt.ParentSpanId);
        Assert.Contains(ShippedSpans, span => span.Source.Name == "Npgsql" && span.TraceId == attempt.TraceId);
    }

    [Fact]
    public async Task A_retry_gets_a_second_span_linked_to_the_same_request()
    {
        Brevo.Answer(HttpStatusCode.InternalServerError);
        using var response = await SignUpAsync(SignUpBody(NewEmail()));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AwaitLogsAsync("DeliveryRetrying", 1);
        AdvanceDeliveries(TimeSpan.FromSeconds(30));

        var attempts = await AwaitSpansAsync(WorkerSpans.MessageSource, 2);
        var request = ServerSpan("api/v1/auth/sign-up");
        Assert.Equal(2, attempts.Count);
        Assert.NotEqual(attempts[0].TraceId, attempts[1].TraceId);
        Assert.All(attempts, attempt => Assert.Equal(request.SpanId, Assert.Single(attempt.Links).Context.SpanId));
    }

    [Fact]
    public async Task A_delivery_queued_outside_a_request_gets_a_span_with_no_link()
    {
        await AddMessageAsync(PasswordNotice());

        var attempt = Assert.Single(await AwaitSpansAsync(WorkerSpans.MessageSource, 1));
        Assert.Empty(attempt.Links);
        Assert.Equal(default, attempt.ParentSpanId);
    }

    [Fact]
    public async Task A_copy_job_attempt_links_to_the_request_that_queued_it()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing", Color.Green);
        await CreateCardAsync(source.Id, "Write the brief");

        await AwaitCopyJobAsync((await CopyListAsync(source.Id, board.Id)).Id);

        var attempt = Assert.Single(await AwaitSpansAsync(WorkerSpans.CopyJobSource, 1));
        var request = Assert.Single(ShippedSpans, span =>
            span.Kind == ActivityKind.Server && span.GetTagItem("http.route") is string route
            && route.EndsWith("/copies", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(request.SpanId, Assert.Single(attempt.Links).Context.SpanId);
        Assert.Equal(default, attempt.ParentSpanId);
        Assert.Contains(ShippedSpans, span => span.Source.Name == "Npgsql" && span.TraceId == attempt.TraceId);
    }

    [Fact]
    public async Task An_empty_poll_exports_no_span()
    {
        await AddMessageAsync(PasswordNotice());
        await AwaitDeliveryAsync(DeliveryStatus.Sent);

        Assert.Equal(0, await Services.GetRequiredService<SessionSweeper>().SweepAsync(CancellationToken.None));

        Assert.DoesNotContain(ShippedSpans, span => span.Source.Name == WorkerSpans.WorkerSource);
        Assert.DoesNotContain(ShippedSpans, span => span.Kind == ActivityKind.Client && span.ParentSpanId == default);
    }

    [Fact]
    public async Task A_sweep_that_finds_work_exports_one_root_span_over_its_deletes()
    {
        await FromDatabaseAsync(database => database.Sessions
            .Where(session => session.Id == ActorUser.SessionId)
            .ExecuteUpdateAsync(session => session
                .SetProperty(s => s.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(s => s.EndReason, SessionEndReason.SignedOut)));
        Time.Advance(SessionSweeper.KeptAfterFinish + TimeSpan.FromMinutes(1));

        Assert.Equal(1, await Services.GetRequiredService<SessionSweeper>().SweepAsync(CancellationToken.None));

        var sweep = Assert.Single(ShippedSpans, span => span.Source.Name == WorkerSpans.WorkerSource);
        Assert.Equal(nameof(SessionSweeper), sweep.DisplayName);
        Assert.Equal(default, sweep.ParentSpanId);
        Assert.Contains(ShippedSpans, span => span.Source.Name == "Npgsql" && span.ParentSpanId == sweep.SpanId);
    }

    private async Task<List<Activity>> AwaitSpansAsync(string source, int count)
    {
        for (var poll = 0; poll < 250; poll++)
        {
            var found = ShippedSpans.Where(span => span.Source.Name == source).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException($"{source} shipped fewer than {count} spans.");
    }
}
