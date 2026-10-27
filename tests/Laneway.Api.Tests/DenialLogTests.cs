using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class DenialLogTests : ApiTests
{
    public DenialLogTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_hiding_404_is_logged()
    {
        var stranger = await CreateUserAsync();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Private);

        using var response = await GetAsync($"/api/v1/workspaces/{workspace.Id}", stranger);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var denied = await DeniedAsync(workspace.Id);
        Assert.Equal(stranger.Id.ToString(), denied.GetStructuredStateValue("sub"));
        Assert.Equal("Workspace", denied.GetStructuredStateValue("targetType"));
        Assert.Equal("404", denied.GetStructuredStateValue("status"));
        Assert.Equal(ErrorCodes.NotFound, denied.GetStructuredStateValue("code"));
        Assert.Equal(DenialDrainer.Category, denied.Category);
        Assert.Null(denied.GetStructuredStateValue(ClientIp.Field));
    }

    [Fact]
    public async Task A_denial_carries_the_address_that_sent_the_request()
    {
        var stranger = await CreateUserAsync();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Private);

        using var response = await SendFromAsync("2001:db8::7", HttpMethod.Get, $"/api/v1/workspaces/{workspace.Id}", stranger);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var denied = await DeniedAsync(workspace.Id);
        Assert.Equal("2001:db8::7", denied.GetStructuredStateValue(ClientIp.Field));
    }

    [Fact]
    public async Task A_denial_logs_in_its_request_trace_without_a_span_of_its_own()
    {
        var stranger = await CreateUserAsync();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Private);

        using var response = await GetAsync($"/api/v1/workspaces/{workspace.Id}", stranger);

        await DeniedAsync(workspace.Id);
        var server = ServerSpan("api/v1/workspaces/{id}");
        var shipped = Assert.Single(ShippedLogs, log => log.EventId.Name == "AccessDenied");
        Assert.Equal(server.TraceId, shipped.TraceId);
        Assert.DoesNotContain(ShippedSpans, span => span.SpanId == shipped.SpanId);
    }

    [Fact]
    public async Task A_missing_id_isnt_logged()
    {
        var stranger = await CreateUserAsync();
        var missing = Guid.NewGuid();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Private);

        using var gone = await GetAsync($"/api/v1/boards/{missing}", stranger);
        using var hidden = await GetAsync($"/api/v1/workspaces/{workspace.Id}", stranger);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        await DeniedAsync(workspace.Id);
        Assert.DoesNotContain(Logs(), log => log.GetStructuredStateValue("targetId") == missing.ToString());
    }

    [Fact]
    public async Task A_403_is_logged_with_its_code()
    {
        var outsider = await CreateUserAsync();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Public);

        using var response = await PutAsync(
            $"/api/v1/workspaces/{workspace.Id}", new { name = "Mine now", visibility = "Public" }, outsider);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denied = await DeniedAsync(workspace.Id);
        Assert.Equal(outsider.Id.ToString(), denied.GetStructuredStateValue("sub"));
        Assert.Equal("403", denied.GetStructuredStateValue("status"));
        Assert.Equal(ErrorCodes.RequiresWorkspaceAdmin, denied.GetStructuredStateValue("code"));
    }

    [Fact]
    public async Task A_hidden_body_target_is_logged_as_a_400()
    {
        var (member, list, _, hidden) = await SeedHiddenBoardAsync();

        using var response = await PutAsync($"/api/v1/lists/{list.Id}/position", new { boardId = hidden.Id }, member);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var denied = await DeniedAsync(hidden.Id);
        Assert.Equal(member.Id.ToString(), denied.GetStructuredStateValue("sub"));
        Assert.Equal("Board", denied.GetStructuredStateValue("targetType"));
        Assert.Equal("400", denied.GetStructuredStateValue("status"));
        Assert.Equal(ErrorCodes.NotFound, denied.GetStructuredStateValue("code"));
    }

    [Fact]
    public async Task A_hidden_anchor_is_logged_as_a_400()
    {
        var (member, _, board, hidden) = await SeedHiddenBoardAsync();
        var anchor = await CreateListAsync(hidden.Id, "Hidden");

        using var response = await PostAsync(ListsPath(board.Id), new { name = "Anchored", after = anchor.Id }, member);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var denied = await DeniedAsync(anchor.Id);
        Assert.Equal("List", denied.GetStructuredStateValue("targetType"));
        Assert.Equal("400", denied.GetStructuredStateValue("status"));
    }

    [Fact]
    public async Task Someone_elses_copy_job_is_logged_as_a_404()
    {
        var (member, list, board, _) = await SeedHiddenBoardAsync();
        var job = await CopyListAsync(list.Id, board.Id);

        using var response = await GetAsync(CopyJobPath(job.Id), member);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var denied = await DeniedAsync(job.Id);
        Assert.Equal("CopyJob", denied.GetStructuredStateValue("targetType"));
        Assert.Equal("404", denied.GetStructuredStateValue("status"));
        Assert.Equal(ErrorCodes.NotFound, denied.GetStructuredStateValue("code"));
    }

    [Fact]
    public void A_full_channel_drops_the_entry_and_counts_it()
    {
        var meters = Services.GetRequiredService<IMeterFactory>();
        using var dropped = new MetricCollector<long>(meters, DenialMeter.Name, DenialMeter.DroppedName);
        var log = new DenialLog(Services.GetRequiredService<DenialMeter>(), capacity: 1);

        log.Add(new Denial(ActorId, AccessTarget.Board, Guid.NewGuid(), null));
        log.Add(new Denial(ActorId, AccessTarget.Board, Guid.NewGuid(), null));

        Assert.Equal(1, dropped.GetMeasurementSnapshot().Sum(measurement => measurement.Value));
        Assert.Equal(1, log.Reader.Count);
    }

    private async Task<(TestUser Member, ListDto List, BoardDto Board, BoardDto Hidden)> SeedHiddenBoardAsync()
    {
        var member = await CreateUserAsync();
        var workspace = await CreateWorkspaceAsync("Finance", visibility: WorkspaceVisibility.Private);
        var board = await CreateBoardAsync(workspace.Id, "Budget", BoardVisibility.Private);
        var hidden = await CreateBoardAsync(workspace.Id, "Payroll", BoardVisibility.Private);
        var list = await CreateListAsync(board.Id, "Doing");
        await FromDatabaseAsync(async database =>
        {
            database.BoardMembers.Add(new BoardMember
            {
                Id = Guid.NewGuid(), BoardId = board.Id, UserId = member.Id, Role = BoardRole.Normal, CreatedBy = member.Id
            });
            return await database.SaveChangesAsync();
        });

        return (member, list, board, hidden);
    }

    private async Task<FakeLogRecord> DeniedAsync(Guid targetId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Logs().FirstOrDefault(log => log.Id.Name == "AccessDenied"
                    && log.GetStructuredStateValue("targetId") == targetId.ToString()) is { } found)
            {
                return found;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"No AccessDenied entry for {targetId}.");
    }
}
