using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AccessLogTests : ApiTests
{
    private const string Ip = "198.51.100.7";

    public AccessLogTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Changing_a_workspaces_visibility_logs_the_new_and_previous_visibility()
    {
        var workspace = await CreateWorkspaceAsync("Design", visibility: WorkspaceVisibility.Private);

        using var response = await SendFromAsync(Ip, HttpMethod.Put, $"/api/v1/workspaces/{workspace.Id}", ActorUser,
            new { name = "Design", visibility = "Public" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = Assert.Single(AccessLogs("VisibilityChanged"));
        AssertTarget(log, "Workspace", workspace.Id);
        Assert.Equal(
            ("Public", "Private"),
            (log.GetStructuredStateValue("visibility"), log.GetStructuredStateValue("previousVisibility")));
    }

    [Fact]
    public async Task A_workspace_put_that_keeps_the_visibility_logs_no_visibility_change()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await UpdateWorkspaceAsync(workspace.Id, "Renamed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(AccessLogs("VisibilityChanged"));
    }

    [Fact]
    public async Task Changing_a_boards_visibility_logs_the_new_and_previous_visibility()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap", BoardVisibility.Private);

        using var response = await PutSettingsFromAsync(board.Id, visibility: "Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = Assert.Single(AccessLogs("VisibilityChanged"));
        AssertTarget(log, "Board", board.Id);
        Assert.Equal(
            ("Public", "Private"),
            (log.GetStructuredStateValue("visibility"), log.GetStructuredStateValue("previousVisibility")));
        Assert.Empty(AccessLogs("BoardSettingsChanged"));
    }

    [Fact]
    public async Task Changing_who_may_add_members_and_self_join_logs_both_setting_names()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await PutSettingsFromAsync(board.Id, addingMembers: "Members", selfJoin: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = Assert.Single(AccessLogs("BoardSettingsChanged"));
        AssertTarget(log, "Board", board.Id);
        Assert.Equal("[\"addingMembers\", \"selfJoin\"]", log.GetStructuredStateValue("settings"));
        Assert.Empty(AccessLogs("VisibilityChanged"));
    }

    [Fact]
    public async Task Changing_only_the_commenting_setting_logs_no_access_change()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await PutSettingsFromAsync(board.Id, commenting: "Disabled");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Logs(), log => log.Category == AccessLog.Category);
    }

    [Fact]
    public async Task Deleting_a_board_logs_board_deleted()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        await SetBoardArchivedAsync(board.Id, true);

        using var response = await SendFromAsync(Ip, HttpMethod.Delete, $"/api/v1/boards/{board.Id}", ActorUser);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertTarget(Assert.Single(AccessLogs("BoardDeleted")), "Board", board.Id);
    }

    [Fact]
    public async Task Deleting_a_workspace_logs_workspace_deleted()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await SendFromAsync(Ip, HttpMethod.Delete, $"/api/v1/workspaces/{workspace.Id}", ActorUser);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertTarget(Assert.Single(AccessLogs("WorkspaceDeleted")), "Workspace", workspace.Id);
    }

    [Fact]
    public async Task A_refused_workspace_delete_logs_nothing()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await DeleteAsync($"/api/v1/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(AccessLogs("WorkspaceDeleted"));
    }

    private Task<HttpResponseMessage> PutSettingsFromAsync(
        Guid boardId,
        string commenting = "Members",
        string addingMembers = "Admins",
        bool selfJoin = true,
        string visibility = "Workspace") =>
        SendFromAsync(Ip, HttpMethod.Put, $"/api/v1/boards/{boardId}/settings", ActorUser,
            new { commenting, addingMembers, selfJoin, visibility });

    private void AssertTarget(FakeLogRecord log, string targetType, Guid targetId) =>
        Assert.Equal(
            (ActorId.ToString(), targetType, targetId.ToString(), Ip),
            (log.GetStructuredStateValue("sub"), log.GetStructuredStateValue("targetType"),
                log.GetStructuredStateValue("targetId"), log.GetStructuredStateValue(ClientIp.Field)));
}
