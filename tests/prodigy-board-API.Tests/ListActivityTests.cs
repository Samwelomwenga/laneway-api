using System.Net;
using System.Text.Json;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class ListActivityTests : ApiTests
{
    public ListActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_list_writes_a_create_entry_naming_its_board_and_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing", Color.Blue);

        var page = await ActivityAsync($"listId={list.Id}");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(ActivityType.CreateList, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(list.Id, entry.Data.Id("list.id"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal(nameof(Color.Blue), entry.Data.Text("list.color"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
    }

    [Fact]
    public async Task A_list_put_writes_an_update_entry_holding_only_the_fields_that_changed()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing", Color.Blue);

        using var response = await UpdateListAsync(list.Id, "In progress", Color.Blue);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=updateList");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("In progress", entry.Data.Text("list.name"));
        Assert.Equal("Doing", entry.Data.Text("old.name"));
        Assert.False(entry.Data.Has("old.color"));
    }

    [Fact]
    public async Task An_update_entry_shows_a_colour_that_was_cleared()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing", Color.Blue);

        using var cleared = await UpdateListAsync(list.Id, "Doing");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var painted = await UpdateListAsync(list.Id, "Doing", Color.Red);
        Assert.Equal(HttpStatusCode.OK, painted.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=updateList");

        Assert.Equal(2, page.TotalCount);
        Assert.True(page.Entries[0].Data.Has("old.color"));
        Assert.Equal(JsonValueKind.Null, page.Entries[0].Data.At("old.color").ValueKind);
        Assert.Equal(nameof(Color.Red), page.Entries[0].Data.Text("list.color"));
        Assert.Equal(nameof(Color.Blue), page.Entries[1].Data.Text("old.color"));
        Assert.False(page.Entries[1].Data.Has("list.color"));
    }

    [Fact]
    public async Task A_list_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");

        using var first = await UpdateListAsync(list.Id, "In progress");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var again = await UpdateListAsync(list.Id, "In progress");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=updateList");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Archiving_and_restoring_a_list_write_their_own_entries()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");

        await SetListArchivedAsync(list.Id, true);
        await SetListArchivedAsync(list.Id, false);

        var page = await ActivityAsync($"listId={list.Id}&type=archiveList,restoreList");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.RestoreList, page.Entries[0].Type);
        Assert.Equal(ActivityType.ArchiveList, page.Entries[1].Type);
        ActivityAssert.Place(page.Entries[1], workspaceId: workspace.Id, boardId: board.Id, listId: list.Id);
        Assert.Equal("Doing", page.Entries[1].Data.Text("list.name"));
    }

    [Fact]
    public async Task Sending_the_archived_value_a_list_already_has_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");

        await SetListArchivedAsync(list.Id, true);
        await SetListArchivedAsync(list.Id, true);

        var page = await ActivityAsync($"listId={list.Id}&type=archiveList");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Reordering_a_list_writes_a_move_entry_with_no_source_and_both_positions()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var first = await CreateListAsync(board.Id, "Doing");
        var second = await CreateListAsync(board.Id, "Done");

        using var response = await MoveListAsync(second.Id, board.Id, position: "top");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={second.Id}&type=moveList");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: second.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.False(entry.Data.Has("from"));
        Assert.Equal(second.Position, entry.Data.At("position.old").GetDouble());
        Assert.True(entry.Data.At("position.new").GetDouble() < first.Position);
    }

    [Fact]
    public async Task Moving_a_list_to_the_spot_it_already_holds_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");

        using var response = await MoveListAsync(list.Id, board.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=moveList");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Moving_a_list_to_another_board_records_the_board_it_left()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var list = await CreateListAsync(from.Id, "Doing");

        using var response = await MoveListAsync(list.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=moveList");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: to.Id, listId: list.Id, fromBoardId: from.Id);
        Assert.Equal("Backlog", entry.Data.Text("board.name"));
        Assert.Equal(from.Id, entry.Data.Id("from.board.id"));
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));
        Assert.False(entry.Data.Has("from.workspace"));
    }

    [Fact]
    public async Task A_list_move_between_boards_reads_through_both_boards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var list = await CreateListAsync(from.Id, "Doing");

        using var response = await MoveListAsync(list.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var atSource = await ActivityAsync($"boardId={from.Id}&type=moveList");
        var atDestination = await ActivityAsync($"boardId={to.Id}&type=moveList");

        Assert.Equal(Assert.Single(atSource.Entries).Id, Assert.Single(atDestination.Entries).Id);
    }

    [Fact]
    public async Task A_list_move_across_workspaces_records_the_workspace_it_left()
    {
        var fromWorkspace = await CreateWorkspaceAsync("Design");
        var toWorkspace = await CreateWorkspaceAsync("Research");
        var from = await CreateBoardAsync(fromWorkspace.Id, "Roadmap");
        var to = await CreateBoardAsync(toWorkspace.Id, "Interviews");
        var list = await CreateListAsync(from.Id, "Doing");

        using var response = await MoveListAsync(list.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=moveList");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: toWorkspace.Id,
            boardId: to.Id,
            listId: list.Id,
            fromWorkspaceId: fromWorkspace.Id,
            fromBoardId: from.Id);
        Assert.Equal(fromWorkspace.Id, entry.Data.Id("from.workspace.id"));
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));
    }

    [Fact]
    public async Task A_list_move_stores_the_labels_it_created_and_writes_nothing_for_its_cards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var list = await CreateListAsync(from.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var carried = await CreateLabelAsync(from.Id, "Urgent", Color.Red);
        var matched = await CreateLabelAsync(from.Id, "Bug", Color.Purple);
        await CreateLabelAsync(to.Id, "Bug", Color.Green);
        await AddLabelAsync(card.Id, carried.Id);
        await AddLabelAsync(card.Id, matched.Id);

        using var response = await MoveListAsync(list.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}&type=moveList");

        var created = Assert.Single(page.Entries).Data.At("createdLabels").EnumerateArray().ToList();
        Assert.Equal("Urgent", Assert.Single(created).Text("name"));
        Assert.Equal(nameof(Color.Red), created[0].Text("color"));

        var onCard = await ActivityAsync($"cardId={card.Id}");
        Assert.Equal(0, onCard.TotalCount);
        var labelEntries = await ActivityAsync($"boardId={to.Id}&type=createLabel");
        Assert.Equal(1, labelEntries.TotalCount);
    }

    [Fact]
    public async Task Deleting_a_list_leaves_its_entries_and_writes_a_delete_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        await SetListArchivedAsync(list.Id, true);

        using var response = await DeleteAsync($"/api/v1/lists/{list.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"listId={list.Id}");

        Assert.Equal(3, page.TotalCount);
        var entry = page.Entries[0];
        Assert.Equal(ActivityType.DeleteList, entry.Type);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id);
        Assert.Equal("Doing", entry.Data.Text("list.name"));
    }

    [Fact]
    public async Task A_respread_writes_no_entry_for_the_lists_it_moves()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var crowded = await CreateListAsync(board.Id, "Doing", position: double.Epsilon);

        var pushed = await CreateListAsync(board.Id, "Backlog", position: "top");

        var respread = await ReadListAsync(crowded.Id);
        Assert.True(respread.Position > crowded.Position);
        Assert.True(pushed.Position < respread.Position);
        var page = await ActivityAsync($"listId={crowded.Id}");
        Assert.Equal(ActivityType.CreateList, Assert.Single(page.Entries).Type);
    }
}
