using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CardCopyActivityTests : ApiTests
{
    public CardCopyActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_in_the_same_list_writes_one_entry_naming_both_cards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var source = await CreateCardAsync(list.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, list.Id, title: "Write the second brief");

        var page = await ActivityAsync($"cardId={copy.Id}&type=copyCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: copy.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(copy.Id, entry.Data.Id("card.id"));
        Assert.Equal("Write the second brief", entry.Data.Text("card.title"));
        Assert.Equal(source.Id, entry.Data.Id("source.id"));
        Assert.Equal("Write the brief", entry.Data.Text("source.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("from"));
        Assert.False(entry.Data.Has("labelSwaps"));
    }

    [Fact]
    public async Task A_copy_stores_the_keep_it_resolved()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        var everything = await CopyCardAsync(source.Id, list.Id);
        var nothing = await CopyCardAsync(source.Id, list.Id, keep: []);
        var some = await CopyCardAsync(source.Id, list.Id, keep: ["attachments", "labels"]);

        Assert.Equal(
            [nameof(CopyPart.Labels), nameof(CopyPart.Checklists), nameof(CopyPart.Attachments)],
            await KeepAsync(everything.Id));
        Assert.Empty(await KeepAsync(nothing.Id));
        Assert.Equal([nameof(CopyPart.Labels), nameof(CopyPart.Attachments)], await KeepAsync(some.Id));
    }

    [Fact]
    public async Task A_copy_into_another_list_records_the_list_it_came_from()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var from = await CreateListAsync(board.Id, "Doing");
        var to = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(from.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"cardId={copy.Id}&type=copyCard")).Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: workspace.Id,
            boardId: board.Id,
            listId: to.Id,
            cardId: copy.Id,
            fromListId: from.Id);
        Assert.Equal("Doing", entry.Data.Text("from.list.name"));
        Assert.False(entry.Data.Has("from.board"));
        Assert.False(entry.Data.Has("from.workspace"));
    }

    [Fact]
    public async Task A_copy_onto_another_board_records_the_board_and_its_label_swaps()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Later");
        var red = await CreateLabelAsync(from.Id, "Bug", Color.Red);
        var green = await CreateLabelAsync(from.Id, null, Color.Green);
        var blue = await CreateLabelAsync(to.Id, "bug", Color.Blue);
        var source = await CreateCardAsync(here.Id, "Write the brief", labelIds: [red.Id, green.Id]);

        var copy = await CopyCardAsync(source.Id, there.Id);

        var entry = Assert.Single((await ActivityAsync($"cardId={copy.Id}&type=copyCard")).Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: workspace.Id,
            boardId: to.Id,
            listId: there.Id,
            cardId: copy.Id,
            fromBoardId: from.Id,
            fromListId: here.Id);
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));

        var swaps = entry.Data.At("labelSwaps").EnumerateArray().ToList();
        Assert.Equal(2, swaps.Count);
        var reused = swaps.Find(swap => swap.Id("from.id") == red.Id);
        Assert.Equal(blue.Id, reused.Id("to.id"));
        Assert.False(reused.At("created").GetBoolean());
        var created = swaps.Find(swap => swap.Id("from.id") == green.Id);
        Assert.True(created.At("created").GetBoolean());
        Assert.Contains(created.Id("to.id"), copy.LabelIds);
    }

    [Fact]
    public async Task A_copy_into_another_workspace_records_every_level_that_differs()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Sales");
        var roadmap = await CreateBoardAsync(from.Id, "Roadmap");
        var pipeline = await CreateBoardAsync(to.Id, "Pipeline");
        var here = await CreateListAsync(roadmap.Id, "Doing");
        var there = await CreateListAsync(pipeline.Id, "Later");
        var source = await CreateCardAsync(here.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, there.Id);

        var entry = Assert.Single((await ActivityAsync($"cardId={copy.Id}&type=copyCard")).Entries);
        ActivityAssert.Place(
            entry,
            workspaceId: to.Id,
            boardId: pipeline.Id,
            listId: there.Id,
            cardId: copy.Id,
            fromWorkspaceId: from.Id,
            fromBoardId: roadmap.Id,
            fromListId: here.Id);
        Assert.Equal("Design", entry.Data.Text("from.workspace.name"));
        Assert.Equal("Roadmap", entry.Data.Text("from.board.name"));
        Assert.Equal("Doing", entry.Data.Text("from.list.name"));
    }

    [Fact]
    public async Task The_source_log_shows_the_copy_through_the_list_it_left()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var from = await CreateListAsync(board.Id, "Doing");
        var to = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(from.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, to.Id);

        var entry = Assert.Single((await ActivityAsync($"listId={from.Id}&type=copyCard")).Entries);
        Assert.Equal(copy.Id, entry.CardId);
        Assert.Equal(0, (await ActivityAsync($"cardId={source.Id}&type=copyCard")).TotalCount);
    }

    [Fact]
    public async Task The_rows_a_copy_creates_get_no_entries_of_their_own()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");
        await CreateLinkAttachmentAsync(source.Id, "https://example.com/brief", "The brief");

        var copy = await CopyCardAsync(source.Id, list.Id);

        var entry = Assert.Single((await ActivityAsync($"cardId={copy.Id}")).Entries);
        Assert.Equal(ActivityType.CopyCard, entry.Type);
    }

    [Fact]
    public async Task A_refused_copy_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var here = await CreateListAsync(board.Id, "Doing");
        var there = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(here.Id, "Write the brief");
        await SetListArchivedAsync(there.Id, true);

        using var response = await SendCardCopyAsync(source.Id, new { listId = there.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        Assert.Equal(0, (await ActivityAsync("type=copyCard")).TotalCount);
    }

    private async Task<List<string>> KeepAsync(Guid cardId)
    {
        var entry = Assert.Single((await ActivityAsync($"cardId={cardId}&type=copyCard")).Entries);
        return entry.Data.At("keep").EnumerateArray().Select(part => part.GetString()!).ToList();
    }

    private async Task<ListDto> NewListAsync() =>
        await CreateListAsync((await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap")).Id,
            "Doing");
}
