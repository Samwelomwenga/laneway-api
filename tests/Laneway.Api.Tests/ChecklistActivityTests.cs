using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ChecklistActivityTests : ApiTests
{
    public ChecklistActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Creating_a_checklist_writes_a_create_entry_naming_its_card_list_board_and_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        var page = await ActivityAsync($"cardId={card.Id}&type=createChecklist");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(checklist.Id, entry.Data.Id("checklist.id"));
        Assert.Equal("Steps", entry.Data.Text("checklist.name"));
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
        Assert.False(entry.Data.Has("completion"));
    }

    [Fact]
    public async Task A_checklist_put_writes_an_update_entry_holding_the_old_name()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        using var response = await UpdateChecklistAsync(checklist.Id, "Stages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateChecklist");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Stages", entry.Data.Text("checklist.name"));
        Assert.Equal("Steps", entry.Data.Text("old.name"));
    }

    [Fact]
    public async Task A_checklist_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        using var response = await UpdateChecklistAsync(checklist.Id, "Steps");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateChecklist");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Reordering_a_checklist_writes_a_move_entry_with_no_source_and_both_positions()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var first = await CreateChecklistAsync(card.Id, "Steps");
        var second = await CreateChecklistAsync(card.Id, "Stages");

        using var response = await MoveChecklistAsync(second.Id, position: "top");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveChecklist");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(second.Id, entry.Data.Id("checklist.id"));
        Assert.False(entry.Data.Has("from"));
        Assert.Equal(second.Position, entry.Data.At("position.old").GetDouble());
        Assert.True(entry.Data.At("position.new").GetDouble() < first.Position);
    }

    [Fact]
    public async Task Moving_a_checklist_to_the_spot_it_already_holds_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        using var response = await MoveChecklistAsync(checklist.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=moveChecklist");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Archiving_and_restoring_a_checklist_write_their_own_entries()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        await SetChecklistArchivedAsync(checklist.Id, true);
        await SetChecklistArchivedAsync(checklist.Id, false);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveChecklist,restoreChecklist");

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ActivityType.RestoreChecklist, page.Entries[0].Type);
        Assert.Equal(ActivityType.ArchiveChecklist, page.Entries[1].Type);
        ActivityAssert.Place(
            page.Entries[1],
            workspaceId: workspace.Id,
            boardId: board.Id,
            listId: list.Id,
            cardId: card.Id);
        ActivityAssert.Actor(page.Entries[1], ActorUser);
        Assert.Equal("Steps", page.Entries[1].Data.Text("checklist.name"));
        Assert.Equal("Write the brief", page.Entries[1].Data.Text("card.title"));
        Assert.False(page.Entries[1].Data.Has("completion"));
    }

    [Fact]
    public async Task Sending_the_archived_value_a_checklist_already_has_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        await SetChecklistArchivedAsync(checklist.Id, true);
        await SetChecklistArchivedAsync(checklist.Id, true);

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveChecklist");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Two_archives_of_one_checklist_at_once_write_one_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(
            list.Id, "Write the brief", dueDate: new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        var done = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(done.Id, "Draft the outline", isChecked: true);
        var open = await CreateChecklistAsync(card.Id, "Stages");
        await CreateCheckItemAsync(open.Id, "Book the room");

        var responses = await Task.WhenAll(
            SendChecklistArchivedAsync(open.Id, true),
            SendChecklistArchivedAsync(open.Id, true));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            response.Dispose();
        }

        var page = await ActivityAsync($"cardId={card.Id}&type=archiveChecklist");

        var entry = Assert.Single(page.Entries);
        Assert.False(entry.Data.At("completion.old").GetBoolean());
        Assert.True(entry.Data.At("completion.new").GetBoolean());
    }

    [Fact]
    public async Task A_deleted_checklist_still_reads_its_entries_through_its_cards_id()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");
        await SetChecklistArchivedAsync(checklist.Id, true);

        using var response = await DeleteAsync($"/api/v1/checklists/{checklist.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=deleteChecklist");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(checklist.Id, entry.Data.Id("checklist.id"));
        Assert.Equal("Steps", entry.Data.Text("checklist.name"));
    }

    [Fact]
    public async Task An_entry_keeps_the_checklist_name_it_had_when_it_was_written()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var checklist = await CreateChecklistAsync(card.Id, "Steps");

        using var renamed = await UpdateChecklistAsync(checklist.Id, "Stages");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=createChecklist");

        Assert.Equal("Steps", Assert.Single(page.Entries).Data.Text("checklist.name"));
    }
}
