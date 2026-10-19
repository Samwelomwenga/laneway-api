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
