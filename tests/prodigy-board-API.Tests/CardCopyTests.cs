using System.Net;
using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace prodigy_board_API.Tests;

[Collection(ApiCollection.Name)]
public class CardCopyTests : ApiTests
{
    public CardCopyTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_carries_every_field_the_card_owns()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var due = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var source = await CreateFullCardAsync(list.Id, due);
        await SetCoverAsync(source.Id, color: Color.Green);

        var copy = await CopyCardAsync(source.Id, list.Id);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Write the brief", copy.Title);
        Assert.Equal("Two pages, no more.", copy.Description);
        Assert.Equal(due, copy.DueDate);
        Assert.Equal(due.AddDays(-2), copy.StartDate);
        Assert.Equal(60, copy.DueReminderMinutes);
        Assert.Equal(list.Id, copy.ListId);
        Assert.False(copy.IsArchived);
        Assert.Equal(0, copy.CommentCount);
        Assert.Equal(Color.Green, copy.Cover!.Color);
        Assert.Equal(ActorId, copy.CreatedBy);
        Assert.Null(copy.UpdatedAt);
        Assert.Null(copy.UpdatedBy);
    }

    [Fact]
    public async Task A_copy_with_no_title_takes_the_source_title_with_no_prefix()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, list.Id);

        Assert.Equal("Write the brief", copy.Title);
    }

    [Fact]
    public async Task A_blank_title_takes_the_source_title()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, list.Id, title: "   ");

        Assert.Equal("Write the brief", copy.Title);
    }

    [Fact]
    public async Task A_given_title_replaces_the_source_title()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        var copy = await CopyCardAsync(source.Id, list.Id, title: "Write the second brief");

        Assert.Equal("Write the second brief", copy.Title);
    }

    [Fact]
    public async Task A_title_over_the_limit_is_tooLong()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await SendCardCopyAsync(
            source.Id, new { listId = list.Id, title = new string('x', FieldLimits.CardTitle + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("title", error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
    }

    [Fact]
    public async Task Comments_is_an_unknown_keep_value()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await SendCardCopyAsync(
            source.Id, new { listId = list.Id, keep = new[] { "labels", "comments" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("keep[1]", error.Field);
        Assert.Equal(ErrorCodes.UnknownValue, error.Code);
    }

    [Fact]
    public async Task Another_body_field_is_unknownField()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await SendCardCopyAsync(
            source.Id, new { listId = list.Id, description = "Not a copy field" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("description", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task A_missing_keep_brings_labels_checklists_and_attachments()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var label = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var source = await CreateCardAsync(list.Id, "Write the brief", labelIds: [label.Id]);
        var checklist = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");
        await CreateLinkAttachmentAsync(source.Id, "https://example.com/brief", "The brief");

        var copy = await CopyCardAsync(source.Id, list.Id);

        Assert.Equal([label.Id], copy.LabelIds);
        Assert.Equal(1, copy.CheckItemCount);
        Assert.Equal(1, copy.AttachmentCount);
    }

    [Fact]
    public async Task An_empty_keep_brings_nothing_but_the_cards_own_fields()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "Doing");
        var label = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var source = await CreateCardAsync(list.Id, "Write the brief", labelIds: [label.Id]);
        var checklist = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");
        await CreateLinkAttachmentAsync(source.Id, "https://example.com/brief", "The brief");

        var copy = await CopyCardAsync(source.Id, list.Id, keep: []);

        Assert.Empty(copy.LabelIds);
        Assert.Empty(await ReadChecklistsAsync(copy.Id));
        Assert.Empty(await ReadAttachmentsAsync(copy.Id));
    }

    [Fact]
    public async Task An_archived_card_under_an_archived_list_can_be_copied()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var from = await CreateListAsync(board.Id, "Doing");
        var to = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(from.Id, "Write the brief");
        await SetCardArchivedAsync(source.Id, true);
        await SetListArchivedAsync(from.Id, true);

        var copy = await CopyCardAsync(source.Id, to.Id);

        Assert.False(copy.IsArchived);
        Assert.Equal(to.Id, copy.ListId);
    }

    [Fact]
    public async Task A_copy_lands_at_the_bottom_by_default()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var last = await CreateCardAsync(list.Id, "Book the room");

        var copy = await CopyCardAsync(source.Id, list.Id);

        Assert.True(copy.Position > last.Position);
    }

    [Fact]
    public async Task An_archived_card_works_as_an_anchor()
    {
        var list = await NewListAsync();
        var first = await CreateCardAsync(list.Id, "Write the brief");
        var second = await CreateCardAsync(list.Id, "Book the room");
        await SetCardArchivedAsync(first.Id, true);

        var copy = await CopyCardAsync(second.Id, list.Id, after: first.Id);

        Assert.True(copy.Position > first.Position);
        Assert.True(copy.Position < second.Position);
    }

    [Fact]
    public async Task Kept_labels_keep_their_ids_on_the_same_board()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var here = await CreateListAsync(board.Id, "Doing");
        var there = await CreateListAsync(board.Id, "Done");
        var bug = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var green = await CreateLabelAsync(board.Id, null, Color.Green);
        var source = await CreateCardAsync(here.Id, "Write the brief", labelIds: [bug.Id, green.Id]);

        var copy = await CopyCardAsync(source.Id, there.Id);

        Assert.Equal(2, copy.LabelIds.Count);
        Assert.Contains(bug.Id, copy.LabelIds);
        Assert.Contains(green.Id, copy.LabelIds);
    }

    [Fact]
    public async Task A_copy_to_another_board_reuses_a_matching_label_and_creates_the_rest()
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

        Assert.Equal(2, copy.LabelIds.Count);
        Assert.Contains(blue.Id, copy.LabelIds);
        Assert.DoesNotContain(red.Id, copy.LabelIds);
        Assert.DoesNotContain(green.Id, copy.LabelIds);
        var read = await ReadCardAsync(source.Id);
        Assert.Equal(2, read.LabelIds.Count);
        Assert.Contains(red.Id, read.LabelIds);
        Assert.Contains(green.Id, read.LabelIds);
    }

    [Fact]
    public async Task Kept_checklists_bring_their_items_their_checked_state_and_their_positions()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var steps = await CreateChecklistAsync(source.Id, "Steps");
        var done = await CreateCheckItemAsync(steps.Id, "Draft it", isChecked: true);
        var todo = await CreateCheckItemAsync(steps.Id, "Send it");
        var put = await CreateChecklistAsync(source.Id, "Put away");
        await SetChecklistArchivedAsync(put.Id, true);

        var copy = await CopyCardAsync(source.Id, list.Id);

        var checklist = Assert.Single(await ReadChecklistsAsync(copy.Id));
        Assert.Equal("Steps", checklist.Name);
        Assert.Equal(steps.Position, checklist.Position);
        Assert.NotEqual(steps.Id, checklist.Id);
        Assert.Equal(2, checklist.CheckItems.Count);
        Assert.Equal(["Draft it", "Send it"], checklist.CheckItems.ConvertAll(item => item.Name));
        Assert.Equal([true, false], checklist.CheckItems.ConvertAll(item => item.IsChecked));
        Assert.Equal([done.Position, todo.Position], checklist.CheckItems.ConvertAll(item => item.Position));
    }

    [Fact]
    public async Task A_copy_that_drops_the_checklists_still_shows_what_the_source_shows()
    {
        var list = await NewListAsync();
        var due = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var source = await CreateCardAsync(list.Id, "Write the brief", dueDate: due);
        var steps = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it", isChecked: true);
        Assert.True((await ReadCardAsync(source.Id)).IsDueComplete);

        var copy = await CopyCardAsync(source.Id, list.Id, keep: []);

        Assert.True(copy.IsDueComplete);
        Assert.Equal(0, copy.CheckItemCount);
    }

    [Fact]
    public async Task Kept_links_carry_their_url_and_name()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        await CreateLinkAttachmentAsync(source.Id, "https://example.com/brief", "The brief");

        var copy = await CopyCardAsync(source.Id, list.Id);

        var attachment = Assert.Single(await ReadAttachmentsAsync(copy.Id));
        Assert.Equal(AttachmentKind.Link, attachment.Kind);
        Assert.Equal("https://example.com/brief", attachment.Url);
        Assert.Equal("The brief", attachment.Name);
        Assert.Equal(copy.Id, attachment.CardId);
    }

    [Fact]
    public async Task A_kept_file_gets_its_own_object_under_the_new_card()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var uploaded = await UploadAttachmentAsync(source.Id, "shot.png", "The mock");

        var copy = await CopyCardAsync(source.Id, list.Id);

        var attachment = Assert.Single(await ReadAttachmentsAsync(copy.Id));
        Assert.NotEqual(uploaded.Id, attachment.Id);
        Assert.Equal("The mock", attachment.Name);
        Assert.Equal("shot.png", attachment.FileName);
        Assert.Equal(uploaded.MimeType, attachment.MimeType);
        Assert.Equal(uploaded.Bytes, attachment.Bytes);
        Assert.Equal(
            await DownloadAttachmentAsync(source.Id, uploaded.Id),
            await DownloadAttachmentAsync(copy.Id, attachment.Id));
    }

    [Fact]
    public async Task A_copy_clears_the_outbox_rows_it_wrote_for_its_new_objects()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        await UploadAttachmentAsync(source.Id, "shot.png", "The mock");

        await CopyCardAsync(source.Id, list.Id);

        Assert.Empty(await FromDatabaseAsync(context => context.PendingObjectDeletes.ToListAsync()));
    }

    [Fact]
    public async Task A_cover_points_at_the_copied_attachment()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var uploaded = await UploadAttachmentAsync(source.Id, "shot.png", "The mock");
        await SetCoverAsync(source.Id, attachmentId: uploaded.Id);

        var copy = await CopyCardAsync(source.Id, list.Id);

        var attachment = Assert.Single(await ReadAttachmentsAsync(copy.Id));
        Assert.Equal(attachment.Id, copy.Cover!.AttachmentId);
        Assert.NotEqual(uploaded.Id, copy.Cover.AttachmentId);
    }

    [Fact]
    public async Task A_copy_without_attachments_keeps_the_cover_color_and_drops_the_cover_image()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var uploaded = await UploadAttachmentAsync(source.Id, "shot.png", "The mock");
        await SetCoverAsync(source.Id, attachmentId: uploaded.Id);

        var withColor = await CreateCardAsync(list.Id, "Book the room");
        await SetCoverAsync(withColor.Id, color: Color.Green);

        var withoutFiles = await CopyCardAsync(source.Id, list.Id, keep: ["labels", "checklists"]);
        var coloured = await CopyCardAsync(withColor.Id, list.Id, keep: []);

        Assert.Null(withoutFiles.Cover);
        Assert.Equal(Color.Green, coloured.Cover!.Color);
    }

    [Fact]
    public async Task Comments_never_come_along()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        await CreateCommentAsync(source.Id, "Looks good.");

        var copy = await CopyCardAsync(source.Id, list.Id);

        Assert.Equal(0, copy.CommentCount);
        Assert.Equal(1, (await ReadCardAsync(source.Id)).CommentCount);
    }

    [Fact]
    public async Task A_copy_leaves_the_source_alone()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");

        await CopyCardAsync(source.Id, list.Id, title: "A second brief");

        var read = await ReadCardAsync(source.Id);
        Assert.Equal("Write the brief", read.Title);
        Assert.Null(read.UpdatedAt);
        Assert.Single(await ReadChecklistsAsync(source.Id));
    }

    [Fact]
    public async Task The_rows_a_copy_creates_sit_one_microsecond_apart_in_the_sources_order()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var first = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(first.Id, "Draft it");
        var second = await CreateChecklistAsync(source.Id, "Later");

        var copy = await CopyCardAsync(source.Id, list.Id);

        var checklists = await ReadChecklistsAsync(copy.Id);
        var stamps = checklists
            .SelectMany(checklist => checklist.CheckItems.Select(item => item.CreatedAt).Append(checklist.CreatedAt))
            .Append(copy.CreatedAt)
            .Order()
            .ToList();

        Assert.Equal(4, stamps.Count);
        Assert.Equal(stamps.Count, stamps.Distinct().Count());
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond * 3), stamps[^1] - stamps[0]);
        Assert.Equal(copy.CreatedAt, stamps[0]);
        Assert.True(checklists.Find(checklist => checklist.Name == "Steps")!.CreatedAt
                    < checklists.Find(checklist => checklist.Name == "Later")!.CreatedAt);
        Assert.Equal(second.Position, checklists.Find(checklist => checklist.Name == "Later")!.Position);
    }

    [Fact]
    public async Task Every_created_row_names_the_actor_and_has_no_update_stamp()
    {
        var other = await CreateUserAsync();
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var checklist = await CreateChecklistAsync(source.Id, "Steps");
        await CreateCheckItemAsync(checklist.Id, "Draft it");
        await CreateLinkAttachmentAsync(source.Id, "https://example.com/brief", "The brief");

        using var response = await SendCardCopyAsync(source.Id, new { listId = list.Id }, other.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.ReadEnvelope<CardDto>()).Data!;

        Assert.Equal(other.Id, copy.CreatedBy);
        Assert.Null(copy.UpdatedBy);
        var copied = Assert.Single(await ReadChecklistsAsync(copy.Id));
        Assert.Equal(other.Id, copied.CreatedBy);
        Assert.Null(copied.UpdatedAt);
        Assert.Equal(other.Id, Assert.Single(copied.CheckItems).CreatedBy);
        Assert.Equal(other.Id, Assert.Single(await ReadAttachmentsAsync(copy.Id)).CreatedBy);
    }

    [Fact]
    public async Task A_copy_needs_an_actor()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");

        using var response = await SendWithoutActorAsync(
            HttpMethod.Post, CopyPath(source.Id), new { listId = list.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ActorFilter.HeaderName, error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task The_body_is_read_before_the_source_is_looked_up()
    {
        using var response = await SendCardCopyAsync(Guid.NewGuid(), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("listId", error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task An_unknown_source_is_404_before_the_destination_is_checked()
    {
        using var response = await SendCardCopyAsync(Guid.NewGuid(), new { listId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_destination_is_notFound_on_listId()
    {
        var list = await NewListAsync();
        var source = await CreateCardAsync(list.Id, "Write the brief");
        var missing = Guid.NewGuid();

        using var response = await SendCardCopyAsync(source.Id, new { listId = missing });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("listId", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task An_anchor_in_another_list_is_notSibling()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var here = await CreateListAsync(board.Id, "Doing");
        var there = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(here.Id, "Write the brief");

        using var response = await SendCardCopyAsync(source.Id, new { listId = there.Id, after = source.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task Anchors_that_are_not_neighbours_are_notAdjacent()
    {
        var list = await NewListAsync();
        var first = await CreateCardAsync(list.Id, "Write the brief");
        await CreateCardAsync(list.Id, "Book the room");
        var third = await CreateCardAsync(list.Id, "Send the notes");

        using var response = await SendCardCopyAsync(
            first.Id, new { listId = list.Id, after = first.Id, before = third.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("before", error.Field);
        Assert.Equal(ErrorCodes.NotAdjacent, error.Code);
    }

    [Fact]
    public async Task An_archived_destination_is_409_on_listId()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var here = await CreateListAsync(board.Id, "Doing");
        var there = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(here.Id, "Write the brief");
        await SetListArchivedAsync(there.Id, true);

        using var response = await SendCardCopyAsync(source.Id, new { listId = there.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("listId", error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
    }

    [Fact]
    public async Task A_bad_anchor_beats_the_archived_destination()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var here = await CreateListAsync(board.Id, "Doing");
        var there = await CreateListAsync(board.Id, "Done");
        var source = await CreateCardAsync(here.Id, "Write the brief");
        await SetListArchivedAsync(there.Id, true);

        using var response = await SendCardCopyAsync(
            source.Id, new { listId = there.Id, after = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    private async Task<ListDto> NewListAsync() =>
        await CreateListAsync((await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap")).Id,
            "Doing");

    private async Task<CardDto> CreateFullCardAsync(Guid listId, DateTime due)
    {
        using var response = await PostAsync("/api/v1/cards", new
        {
            title = "Write the brief",
            description = "Two pages, no more.",
            listId,
            dueDate = due,
            startDate = due.AddDays(-2),
            dueReminderMinutes = 60,
            isDueComplete = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<CardDto>()).Data!;
    }
}
