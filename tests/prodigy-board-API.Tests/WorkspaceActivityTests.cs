using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class WorkspaceActivityTests : ApiTests
{
    public WorkspaceActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_workspace_writes_a_create_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design", "The design team's boards");

        var page = await ActivityAsync($"workspaceId={workspace.Id}");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(ActivityType.CreateWorkspace, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(workspace.Id, entry.Data.Id("workspace.id"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("old"));
        Assert.Null(entry.Text);
        Assert.Null(entry.UpdatedAt);
    }

    [Fact]
    public async Task A_workspace_put_writes_an_update_entry_holding_only_the_fields_that_changed()
    {
        var workspace = await CreateWorkspaceAsync("Design", "The design team's boards");

        using var response = await UpdateWorkspaceAsync(
            workspace.Id, "Design and research", "The design team's boards");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=updateWorkspace");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Design and research", entry.Data.Text("workspace.name"));
        Assert.Equal("Design", entry.Data.Text("old.name"));
        Assert.False(entry.Data.Has("old.description"));
        Assert.False(entry.Data.Has("old.visibility"));
    }

    [Fact]
    public async Task An_update_entry_holds_every_field_a_put_changed()
    {
        var workspace = await CreateWorkspaceAsync("Design", "The design team's boards");

        using var response = await UpdateWorkspaceAsync(
            workspace.Id, "Research", "Interviews and notes", WorkspaceVisibility.Private);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=updateWorkspace");

        var entry = Assert.Single(page.Entries);
        Assert.Equal("Design", entry.Data.Text("old.name"));
        Assert.Equal("The design team's boards", entry.Data.Text("old.description"));
        Assert.Equal(nameof(WorkspaceVisibility.Public), entry.Data.Text("old.visibility"));
    }

    [Fact]
    public async Task A_put_that_changes_nothing_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design", "The design team's boards");

        using var first = await UpdateWorkspaceAsync(workspace.Id, "Research", "Interviews and notes");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var again = await UpdateWorkspaceAsync(workspace.Id, "Research", "Interviews and notes");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=updateWorkspace");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Deleting_a_workspace_leaves_its_entries_and_writes_a_delete_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await DeleteAsync($"/api/v1/workspaces/{workspace.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}");

        Assert.Equal(2, page.TotalCount);
        var entry = page.Entries[0];
        Assert.Equal(ActivityType.DeleteWorkspace, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(workspace.Id, entry.Data.Id("workspace.id"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.Equal(ActivityType.CreateWorkspace, page.Entries[1].Type);
    }

    [Fact]
    public async Task An_entry_keeps_the_name_the_workspace_had_when_it_was_written()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        using var renamed = await UpdateWorkspaceAsync(workspace.Id, "Research");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=createWorkspace");

        Assert.Equal("Design", Assert.Single(page.Entries).Data.Text("workspace.name"));
    }
}
