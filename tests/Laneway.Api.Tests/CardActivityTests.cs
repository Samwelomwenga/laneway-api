using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CardActivityTests : ApiTests
{
    public CardActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_card_writes_a_create_entry_naming_its_list_board_and_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var page = await ActivityAsync($"cardId={card.Id}");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(ActivityType.CreateCard, entry.Type);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(card.Id, entry.Data.Id("card.id"));
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("labels"));
    }

    [Fact]
    public async Task A_card_created_with_labels_names_them()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        var card = await CreateCardAsync(list.Id, "Write the brief", labelIds: [label.Id]);

        var page = await ActivityAsync($"cardId={card.Id}&type=createCard");

        var labels = Assert.Single(page.Entries).Data.At("labels").EnumerateArray().ToList();
        Assert.Equal(label.Id, Assert.Single(labels).Id("id"));
        Assert.Equal("Urgent", labels[0].Text("name"));
        Assert.Equal(nameof(Color.Red), labels[0].Text("color"));
    }

    [Fact]
    public async Task A_card_put_writes_an_update_entry_holding_only_the_fields_that_changed()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await UpdateCardAsync(card.Id, "Write the outline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Write the outline", entry.Data.Text("card.title"));
        Assert.Equal("Write the brief", entry.Data.Text("old.title"));
        Assert.False(entry.Data.Has("old.description"));
        Assert.False(entry.Data.Has("old.dueDate"));
        Assert.False(entry.Data.Has("old.labels"));
    }

    [Fact]
    public async Task An_update_entry_shows_a_due_date_that_was_cleared_and_one_that_was_added()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var due = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var card = await CreateCardAsync(list.Id, "Write the brief", dueDate: due);

        using var cleared = await UpdateCardAsync(card.Id, "Write the brief");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var added = await UpdateCardAsync(card.Id, "Write the brief", dueDate: due);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(JsonValueKind.Null, page.Entries[0].Data.At("old.dueDate").ValueKind);
        Assert.Equal(due, page.Entries[1].Data.At("old.dueDate").GetDateTime());
    }

    [Fact]
    public async Task A_card_put_that_only_swaps_labels_records_both_sets()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var dropped = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        var added = await CreateLabelAsync(board.Id, "Blocking", Color.Purple);
        var card = await CreateCardAsync(list.Id, "Write the brief", labelIds: [dropped.Id]);

        using var response = await UpdateCardAsync(card.Id, "Write the brief", labelIds: [added.Id]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        var entry = Assert.Single(page.Entries);
        Assert.Equal("Urgent", Assert.Single(entry.Data.At("old.labels").EnumerateArray()).Text("name"));
        Assert.Equal("Blocking", Assert.Single(entry.Data.At("labels").EnumerateArray()).Text("name"));
        Assert.False(entry.Data.Has("old.title"));
    }

    [Fact]
    public async Task A_card_put_that_takes_every_label_off_spells_out_the_empty_set()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        var card = await CreateCardAsync(list.Id, "Write the brief", labelIds: [label.Id]);

        using var response = await UpdateCardAsync(card.Id, "Write the brief", labelIds: []);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        var entry = Assert.Single(page.Entries);
        Assert.Equal("Urgent", Assert.Single(entry.Data.At("old.labels").EnumerateArray()).Text("name"));
        Assert.Empty(entry.Data.At("labels").EnumerateArray());
    }

    [Fact]
    public async Task A_card_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        using var first = await UpdateCardAsync(card.Id, "Write the outline");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var again = await UpdateCardAsync(card.Id, "Write the outline");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCard");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Archiving_and_restoring_a_card_write_their_own_entries()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        await SetCardArchivedAsync(card.Id, true);
        await SetCardArchivedAsync(card.Id, false);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveCard,restoreCard");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.RestoreCard, page.Entries[0].Type);
        Assert.Equal(ActivityType.ArchiveCard, page.Entries[1].Type);
        ActivityAssert.Place(
            page.Entries[1],
            workspaceId: workspace.Id,
            boardId: board.Id,
            listId: list.Id,
            cardId: card.Id);
        Assert.Equal("Write the brief", page.Entries[1].Data.Text("card.title"));
        Assert.Equal("Doing", page.Entries[1].Data.Text("list.name"));
    }

    [Fact]
    public async Task Sending_the_archived_value_a_card_already_has_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        await SetCardArchivedAsync(card.Id, true);
        await SetCardArchivedAsync(card.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveCard");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task A_deleted_card_still_reads_its_whole_log_through_its_own_id()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        await SetCardArchivedAsync(card.Id, true);

        using var response = await DeleteAsync($"/api/v1/cards/{card.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}");

        Assert.Equal(3, page.TotalCount);
        var entry = page.Entries[0];
        Assert.Equal(ActivityType.DeleteCard, entry.Type);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
    }

    [Fact]
    public async Task Adding_a_label_to_a_card_writes_an_entry_with_the_labels_name_and_colour()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        await AddLabelAsync(card.Id, label.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=addLabelToCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(label.Id, entry.Data.Id("label.id"));
        Assert.Equal("Urgent", entry.Data.Text("label.name"));
        Assert.Equal(nameof(Color.Red), entry.Data.Text("label.color"));
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
    }

    [Fact]
    public async Task Removing_a_label_from_a_card_writes_an_entry_naming_it()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);
        await AddLabelAsync(card.Id, label.Id);

        await RemoveLabelAsync(card.Id, label.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=removeLabelFromCard");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(label.Id, entry.Data.Id("label.id"));
        Assert.Equal("Urgent", entry.Data.Text("label.name"));
    }

    [Fact]
    public async Task Adding_a_label_the_card_already_has_writes_no_second_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        await AddLabelAsync(card.Id, label.Id);
        await AddLabelAsync(card.Id, label.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=addLabelToCard");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Removing_a_label_the_card_doesnt_have_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var label = await CreateLabelAsync(board.Id, "Urgent", Color.Red);

        await RemoveLabelAsync(card.Id, label.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=removeLabelFromCard");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task An_entry_keeps_the_card_title_it_had_when_it_was_written()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        using var renamed = await UpdateCardAsync(card.Id, "Write the outline");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=createCard");

        Assert.Equal("Write the brief", Assert.Single(page.Entries).Data.Text("card.title"));
    }
}
