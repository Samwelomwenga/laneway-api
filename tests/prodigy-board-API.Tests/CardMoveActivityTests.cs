using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class CardMoveActivityTests : ApiTests
{
    public CardMoveActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Reordering_a_card_writes_a_move_entry_with_no_source_and_both_positions()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var first = await CreateCardAsync(list.Id, "Write the brief");
        var second = await CreateCardAsync(list.Id, "Book the room");

        using var response = await MoveCardAsync(second.Id, list.Id, position: "top");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={second.Id}&type=moveCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: second.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.False(entry.Data.Has("from"));
        Assert.Equal(second.Position, entry.Data.At("position.old").GetDouble());
        Assert.True(entry.Data.At("position.new").GetDouble() < first.Position);
    }

    [Fact]
    public async Task Moving_a_card_to_the_spot_it_already_holds_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await MoveCardAsync(card.Id, list.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCard");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Moving_a_card_to_another_list_on_its_board_records_the_list_it_left()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var from = await CreateListAsync(board.Id, "Doing");
        var to = await CreateListAsync(board.Id, "Done");
        var card = await CreateCardAsync(from.Id, "Write the brief");

        using var response = await MoveCardAsync(card.Id, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: workspace.Id,
            boardId: board.Id,
            listId: to.Id,
            cardId: card.Id,
            fromListId: from.Id);
        Assert.Equal("Done", entry.Data.Text("list.name"));
        Assert.Equal(from.Id, entry.Data.Id("from.list.id"));
        Assert.Equal("Doing", entry.Data.Text("from.list.name"));
        Assert.False(entry.Data.Has("from.board"));
        Assert.False(entry.Data.Has("from.workspace"));
    }

    [Fact]
    public async Task Moving_a_card_to_another_board_records_the_board_and_the_list_it_left()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Later");
        var card = await CreateCardAsync(here.Id, "Write the brief");

        using var response = await MoveCardAsync(card.Id, there.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: workspace.Id,
            boardId: to.Id,
            listId: there.Id,
            cardId: card.Id,
            fromBoardId: from.Id,
            fromListId: here.Id);
        Assert.Equal("Backlog", entry.Data.Text("board.name"));
        Assert.Equal(from.Id, entry.Data.Id("from.board.id"));
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));
        Assert.Equal("Doing", entry.Data.Text("from.list.name"));
        Assert.False(entry.Data.Has("from.workspace"));
    }

    [Fact]
    public async Task A_card_move_between_boards_reads_through_both_boards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Later");
        var card = await CreateCardAsync(here.Id, "Write the brief");

        using var response = await MoveCardAsync(card.Id, there.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var atSource = await ActivityAsync($"boardId={from.Id}&type=moveCard");
        var atDestination = await ActivityAsync($"boardId={to.Id}&type=moveCard");

        Assert.Equal(Assert.Single(atSource.Entries).Id, Assert.Single(atDestination.Entries).Id);
    }

    [Fact]
    public async Task A_card_move_across_workspaces_records_the_workspace_it_left()
    {
        var fromWorkspace = await CreateWorkspaceAsync("Design");
        var toWorkspace = await CreateWorkspaceAsync("Research");
        var from = await CreateBoardAsync(fromWorkspace.Id, "Roadmap");
        var to = await CreateBoardAsync(toWorkspace.Id, "Interviews");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Later");
        var card = await CreateCardAsync(here.Id, "Write the brief");

        using var response = await MoveCardAsync(card.Id, there.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: toWorkspace.Id,
            boardId: to.Id,
            listId: there.Id,
            cardId: card.Id,
            fromWorkspaceId: fromWorkspace.Id,
            fromBoardId: from.Id,
            fromListId: here.Id);
        Assert.Equal(fromWorkspace.Id, entry.Data.Id("from.workspace.id"));
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));
    }

    [Fact]
    public async Task A_card_move_to_another_board_stores_every_label_swap_and_no_create_label_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Later");
        var carried = await CreateLabelAsync(from.Id, "Urgent", Color.Red);
        var matched = await CreateLabelAsync(from.Id, "Bug", Color.Purple);
        var waiting = await CreateLabelAsync(to.Id, "Bug", Color.Green);
        var card = await CreateCardAsync(here.Id, "Write the brief", labelIds: [carried.Id, matched.Id]);

        using var response = await MoveCardAsync(card.Id, there.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveCard");

        var swaps = Assert.Single(page.Entries).Data.At("labelSwaps").EnumerateArray().ToList();
        Assert.Equal(2, swaps.Count);
        var made = Assert.Single(swaps, swap => swap.At("created").GetBoolean());
        Assert.Equal(carried.Id, made.Id("from.id"));
        Assert.Equal("Urgent", made.Text("to.name"));
        Assert.NotEqual(carried.Id, made.Id("to.id"));
        var reused = Assert.Single(swaps, swap => !swap.At("created").GetBoolean());
        Assert.Equal(matched.Id, reused.Id("from.id"));
        Assert.Equal(waiting.Id, reused.Id("to.id"));

        var labelEntries = await ActivityAsync($"boardId={to.Id}&type=createLabel");
        Assert.Equal(1, labelEntries.TotalCount);
    }

    [Fact]
    public async Task A_respread_writes_no_entry_for_the_cards_it_moves()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var crowded = await CreateCardAsync(list.Id, "Write the brief", position: double.Epsilon);

        var pushed = await CreateCardAsync(list.Id, "Book the room", position: "top");

        var respread = await ReadCardAsync(crowded.Id);
        Assert.True(respread.Position > crowded.Position);
        Assert.True(pushed.Position < respread.Position);
        var page = await ActivityAsync($"cardId={crowded.Id}");
        Assert.Equal(ActivityType.CreateCard, Assert.Single(page.Entries).Type);
    }
}
