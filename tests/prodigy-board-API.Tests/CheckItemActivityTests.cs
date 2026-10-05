using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class CheckItemActivityTests : ApiTests
{
    public CheckItemActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_check_item_writes_a_create_entry_naming_its_checklist_and_the_tree_above_it()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        var page = await ActivityAsync($"cardId={card.Id}&type=createCheckItem");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(checkItem.Id, entry.Data.Id("checkItem.id"));
        Assert.Equal("Draft the outline", entry.Data.Text("checkItem.name"));
        Assert.Equal(checklist.Id, entry.Data.Id("checklist.id"));
        Assert.Equal("Steps", entry.Data.Text("checklist.name"));
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("completion"));
    }

    [Fact]
    public async Task A_check_item_put_writes_an_update_entry_holding_the_old_name()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var response = await UpdateCheckItemAsync(checkItem.Id, "Draft the brief");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCheckItem");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Draft the brief", entry.Data.Text("checkItem.name"));
        Assert.Equal("Draft the outline", entry.Data.Text("old.name"));
    }

    [Fact]
    public async Task A_check_item_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var response = await UpdateCheckItemAsync(checkItem.Id, "Draft the outline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCheckItem");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Reordering_a_check_item_writes_a_move_entry_with_no_source_and_both_positions()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var first = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        var second = await CreateCheckItemAsync(checklist.Id, "Book the room");

        using var response = await MoveCheckItemAsync(second.Id, checklist.Id, position: "top");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCheckItem");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(second.Id, entry.Data.Id("checkItem.id"));
        Assert.Equal(checklist.Id, entry.Data.Id("checklist.id"));
        Assert.False(entry.Data.Has("from"));
        Assert.Equal(second.Position, entry.Data.At("position.old").GetDouble());
        Assert.True(entry.Data.At("position.new").GetDouble() < first.Position);
    }

    [Fact]
    public async Task Moving_a_check_item_to_another_checklist_records_the_checklist_it_left()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var from = await CreateChecklistAsync(card.Id, "Steps");
        var to = await CreateChecklistAsync(card.Id, "Stages");
        var checkItem = await CreateCheckItemAsync(from.Id, "Draft the outline");

        using var response = await MoveCheckItemAsync(checkItem.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCheckItem");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        Assert.Equal(to.Id, entry.Data.Id("checklist.id"));
        Assert.Equal("Stages", entry.Data.Text("checklist.name"));
        Assert.Equal(from.Id, entry.Data.Id("from.checklist.id"));
        Assert.Equal("Steps", entry.Data.Text("from.checklist.name"));
    }

    [Fact]
    public async Task Moving_a_check_item_to_the_spot_it_already_holds_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var response = await MoveCheckItemAsync(checkItem.Id, checklist.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCheckItem");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Checking_and_unchecking_a_check_item_write_their_own_entries()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        await SetCheckedAsync(checkItem.Id, true);
        await SetCheckedAsync(checkItem.Id, false);

        var page = await ActivityAsync($"cardId={card.Id}&type=checkCheckItem,uncheckCheckItem");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.UncheckCheckItem, page.Entries[0].Type);
        Assert.Equal(ActivityType.CheckCheckItem, page.Entries[1].Type);
        ActivityAssert.Place(
            page.Entries[1],
            workspaceId: workspace.Id,
            boardId: board.Id,
            listId: list.Id,
            cardId: card.Id);
        ActivityAssert.Actor(page.Entries[1], ActorUser);
        Assert.Equal("Draft the outline", page.Entries[1].Data.Text("checkItem.name"));
        Assert.Equal("Steps", page.Entries[1].Data.Text("checklist.name"));
        Assert.False(page.Entries[1].Data.Has("completion"));
    }

    [Fact]
    public async Task Sending_the_checked_value_a_check_item_already_has_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        await SetCheckedAsync(checkItem.Id, true);
        await SetCheckedAsync(checkItem.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=checkCheckItem");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task A_deleted_check_item_still_reads_its_entries_through_its_cards_id()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var response = await DeleteAsync($"/api/v1/check-items/{checkItem.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=deleteCheckItem");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(checkItem.Id, entry.Data.Id("checkItem.id"));
        Assert.Equal("Draft the outline", entry.Data.Text("checkItem.name"));
        Assert.Equal("Steps", entry.Data.Text("checklist.name"));
    }

    [Fact]
    public async Task An_entry_keeps_the_check_item_name_it_had_when_it_was_written()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        var checkItem = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var renamed = await UpdateCheckItemAsync(checkItem.Id, "Draft the brief");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=createCheckItem");

        Assert.Equal("Draft the outline", Assert.Single(page.Entries).Data.Text("checkItem.name"));
    }
}
