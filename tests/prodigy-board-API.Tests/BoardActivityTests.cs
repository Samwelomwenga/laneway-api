using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class BoardActivityTests : ApiTests
{
    public BoardActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_board_writes_a_create_entry_naming_its_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        var page = await ActivityAsync($"boardId={board.Id}");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(ActivityType.CreateBoard, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(board.Id, entry.Data.Id("board.id"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal(workspace.Id, entry.Data.Id("workspace.id"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("old"));
    }

    [Fact]
    public async Task A_board_create_entry_shows_in_its_workspace_log()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        var page = await ActivityAsync($"workspaceId={workspace.Id}&type=createBoard");

        Assert.Equal(board.Id, Assert.Single(page.Entries).BoardId);
    }

    [Fact]
    public async Task A_board_put_writes_an_update_entry_holding_only_the_fields_that_changed()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await UpdateBoardAsync(board.Id, "Roadmap 2026", "Next year's work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=updateBoard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Roadmap 2026", entry.Data.Text("board.name"));
        Assert.Equal("Roadmap", entry.Data.Text("old.name"));
        Assert.Equal(string.Empty, entry.Data.Text("old.description"));
        Assert.False(entry.Data.Has("old.visibility"));
    }

    [Fact]
    public async Task A_board_put_that_changes_nothing_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var first = await UpdateBoardAsync(board.Id, "Roadmap 2026");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var again = await UpdateBoardAsync(board.Id, "Roadmap 2026");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=updateBoard");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Archiving_and_restoring_a_board_write_their_own_entries()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        await SetBoardArchivedAsync(board.Id, true);
        await SetBoardArchivedAsync(board.Id, false);

        var page = await ActivityAsync($"boardId={board.Id}&type=archiveBoard,restoreBoard");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.RestoreBoard, page.Entries[0].Type);
        Assert.Equal(ActivityType.ArchiveBoard, page.Entries[1].Type);
        ActivityAssert.Place(page.Entries[1], workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(page.Entries[1], ActorUser);
        Assert.Equal("Roadmap", page.Entries[1].Data.Text("board.name"));
        Assert.Equal("Design", page.Entries[1].Data.Text("workspace.name"));
    }

    [Fact]
    public async Task Sending_the_archived_value_a_board_already_has_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        await SetBoardArchivedAsync(board.Id, true);
        await SetBoardArchivedAsync(board.Id, true);

        var page = await ActivityAsync($"boardId={board.Id}&type=archiveBoard");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Moving_a_board_writes_one_entry_both_workspaces_can_read()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Research");
        var board = await CreateBoardAsync(from.Id, "Roadmap");

        using var response = await MoveBoardAsync(board.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=moveBoard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: to.Id, boardId: board.Id, fromWorkspaceId: from.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Research", entry.Data.Text("workspace.name"));
        Assert.Equal(from.Id, entry.Data.Id("from.workspace.id"));
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));

        var atSource = await ActivityAsync($"workspaceId={from.Id}&type=moveBoard");
        var atDestination = await ActivityAsync($"workspaceId={to.Id}&type=moveBoard");
        Assert.Equal(entry.Id, Assert.Single(atSource.Entries).Id);
        Assert.Equal(entry.Id, Assert.Single(atDestination.Entries).Id);
    }

    [Fact]
    public async Task Moving_a_board_to_the_workspace_it_is_already_in_writes_no_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");

        using var response = await MoveBoardAsync(board.Id, workspace.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=moveBoard");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Deleting_a_board_leaves_its_entries_and_writes_a_delete_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        await SetBoardArchivedAsync(board.Id, true);

        using var response = await DeleteAsync($"/api/v1/boards/{board.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}");

        Assert.Equal(3, page.TotalCount);
        var entry = page.Entries[0];
        Assert.Equal(ActivityType.DeleteBoard, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
    }

    [Fact]
    public async Task An_entry_keeps_the_board_name_it_had_when_it_was_written()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        using var renamed = await UpdateBoardAsync(board.Id, "Retired roadmap");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=createBoard");

        Assert.Equal("Roadmap", Assert.Single(page.Entries).Data.Text("board.name"));
    }
}
