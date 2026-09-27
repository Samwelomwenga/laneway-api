using System.Net;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class LabelActivityTests : ApiTests
{
    public LabelActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_label_writes_a_create_entry_on_its_board()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        var page = await ActivityAsync($"boardId={board.Id}&type=createLabel");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(label.Id, entry.Data.Id("label.id"));
        Assert.Equal("Urgent", entry.Data.Text("label.name"));
        Assert.Equal(nameof(Color.Red), entry.Data.Text("label.color"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
    }

    [Fact]
    public async Task A_label_put_writes_an_update_entry_holding_only_the_fields_that_changed()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        using var response = await UpdateLabelAsync(label.Id, "Blocking", Color.Red);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=updateLabel");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: board.WorkspaceId, boardId: board.Id);
        Assert.Equal("Blocking", entry.Data.Text("label.name"));
        Assert.Equal("Urgent", entry.Data.Text("old.name"));
        Assert.False(entry.Data.Has("old.color"));
    }

    [Fact]
    public async Task An_update_entry_shows_a_label_colour_that_was_cleared()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        using var response = await UpdateLabelAsync(label.Id, "Urgent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=updateLabel");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(nameof(Color.Red), entry.Data.Text("old.color"));
        Assert.False(entry.Data.Has("label.color"));
    }

    [Fact]
    public async Task A_label_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        using var first = await UpdateLabelAsync(label.Id, "Blocking", Color.Red);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var again = await UpdateLabelAsync(label.Id, "Blocking", Color.Red);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=updateLabel");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Deleting_a_label_writes_one_entry_and_none_for_the_cards_that_lose_it()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        await AddLabelAsync(card.Id, label.Id);

        using var response = await DeleteAsync($"/api/v1/labels/{label.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=deleteLabel");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(entry, workspaceId: workspace.Id, boardId: board.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(label.Id, entry.Data.Id("label.id"));
        Assert.Equal("Urgent", entry.Data.Text("label.name"));

        var onCard = await ActivityAsync($"cardId={card.Id}");
        Assert.Equal(0, onCard.TotalCount);
    }

    [Fact]
    public async Task A_label_a_card_move_creates_gets_no_create_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var here = await CreateListAsync(from.Id, "Doing");
        var there = await CreateListAsync(to.Id, "Doing");
        var card = await CreateCardAsync(here.Id, "Write the brief");
        var label = await CreateLabelAsync(from.Id, "Urgent", Color.Red);
        await AddLabelAsync(card.Id, label.Id);

        using var response = await PutAsync(
            $"/api/v1/cards/{card.Id}/position", new { listId = there.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"boardId={to.Id}&type=createLabel");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task An_entry_keeps_the_label_name_it_had_when_it_was_written()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        using var renamed = await UpdateLabelAsync(label.Id, "Blocking", Color.Red);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"boardId={board.Id}&type=createLabel");

        Assert.Equal("Urgent", Assert.Single(page.Entries).Data.Text("label.name"));
    }
}
