using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AttachmentActivityTests : ApiTests
{
    public AttachmentActivityTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Uploading_a_file_writes_an_add_entry_naming_its_card_list_board_and_workspace()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        var page = await ActivityAsync($"cardId={card.Id}&type=addAttachment");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(attachment.Id, entry.Data.Id("attachment.id"));
        Assert.Equal("The mock", entry.Data.Text("attachment.name"));
        Assert.Equal(nameof(AttachmentKind.File), entry.Data.Text("attachment.kind"));
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal("Doing", entry.Data.Text("list.name"));
        Assert.Equal("Roadmap", entry.Data.Text("board.name"));
        Assert.Equal("Design", entry.Data.Text("workspace.name"));
    }

    [Fact]
    public async Task Attaching_a_link_writes_an_add_entry_of_kind_link()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        var attachment = await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief", "The brief");

        var page = await ActivityAsync($"cardId={card.Id}&type=addAttachment");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(attachment.Id, entry.Data.Id("attachment.id"));
        Assert.Equal("The brief", entry.Data.Text("attachment.name"));
        Assert.Equal(nameof(AttachmentKind.Link), entry.Data.Text("attachment.kind"));
    }

    [Fact]
    public async Task Renaming_an_attachment_writes_an_update_entry_holding_the_old_name()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        using var response = await RenameAttachmentAsync(card.Id, attachment.Id, "The final mock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateAttachment");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: board.WorkspaceId, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("The final mock", entry.Data.Text("attachment.name"));
        Assert.Equal("The mock", entry.Data.Text("old.name"));
    }

    [Fact]
    public async Task An_attachment_put_that_changes_nothing_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        using var response = await RenameAttachmentAsync(card.Id, attachment.Id, "The mock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateAttachment");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Deleting_an_attachment_that_was_no_cover_writes_a_delete_entry_without_was_cover()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "notes.txt", "The notes");

        await DeleteAttachmentAsync(card.Id, attachment.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=deleteAttachment");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal(attachment.Id, entry.Data.Id("attachment.id"));
        Assert.Equal("The notes", entry.Data.Text("attachment.name"));
        Assert.False(entry.Data.Has("wasCover"));
    }

    [Fact]
    public async Task Deleting_the_attachment_a_card_covers_itself_with_writes_was_cover()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        await DeleteAttachmentAsync(card.Id, attachment.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=deleteAttachment");

        var entry = Assert.Single(page.Entries);
        Assert.Equal(attachment.Id, entry.Data.Id("attachment.id"));
        Assert.True(entry.Data.At("wasCover").GetBoolean());
    }

    [Fact]
    public async Task Setting_a_colour_cover_writes_an_entry_with_no_old_cover()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");

        await SetCoverAsync(card.Id, color: Color.Blue);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCardCover");

        var entry = Assert.Single(page.Entries);
        ActivityAssert.Place(
            entry, workspaceId: workspace.Id, boardId: board.Id, listId: list.Id, cardId: card.Id);
        ActivityAssert.Actor(entry, ActorUser);
        Assert.Equal("Write the brief", entry.Data.Text("card.title"));
        Assert.Equal(nameof(Color.Blue), entry.Data.Text("cover.color"));
        Assert.False(entry.Data.Has("old"));
    }

    [Fact]
    public async Task Swapping_a_colour_cover_for_an_image_puts_the_colour_under_old()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, color: Color.Blue);

        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCardCover");

        Assert.Equal(2, page.TotalCount);
        var entry = page.Entries[0];
        Assert.Equal(attachment.Id, entry.Data.Id("cover.attachment.id"));
        Assert.Equal("The mock", entry.Data.Text("cover.attachment.name"));
        Assert.Equal(nameof(AttachmentKind.File), entry.Data.Text("cover.attachment.kind"));
        Assert.False(entry.Data.Has("cover.color"));
        Assert.Equal(nameof(Color.Blue), entry.Data.Text("old.color"));
    }

    [Fact]
    public async Task Clearing_an_image_cover_puts_the_attachment_under_old_and_leaves_no_cover()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        await SetCoverAsync(card.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCardCover");

        Assert.Equal(2, page.TotalCount);
        var entry = page.Entries[0];
        Assert.False(entry.Data.Has("cover"));
        Assert.Equal(attachment.Id, entry.Data.Id("old.attachment.id"));
        Assert.Equal("The mock", entry.Data.Text("old.attachment.name"));
    }

    [Fact]
    public async Task Sending_the_cover_a_card_already_has_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCardCover");

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Clearing_a_card_that_has_no_cover_writes_no_entry()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");

        await SetCoverAsync(card.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=updateCardCover");

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_deleted_attachment_still_reads_its_entries_through_its_cards_id()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "Doing")).Id, "Write the brief");
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        using var renamed = await RenameAttachmentAsync(card.Id, attachment.Id, "The final mock");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await DeleteAttachmentAsync(card.Id, attachment.Id);

        var page = await ActivityAsync($"cardId={card.Id}&type=addAttachment,updateAttachment,deleteAttachment");

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("The mock", page.Entries[2].Data.Text("attachment.name"));
        Assert.Equal("The final mock", page.Entries[0].Data.Text("attachment.name"));
    }
}
