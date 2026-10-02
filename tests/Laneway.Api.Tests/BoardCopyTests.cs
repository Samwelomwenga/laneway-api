using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardCopyTests : ApiTests
{
    private const string BulkCards = """
        INSERT INTO "Cards"
            ("Id", "Title", "Description", "ListId", "IsDueComplete", "IsArchived", "Position",
             "CreatedAt", "CreatedBy")
        SELECT gen_random_uuid(), 'Card ' || n, '', {0}, false, false, n * 65536.0, now(), {1}
        FROM generate_series(1, {2}::int) AS n
        """;

    private const string BulkFiles = """
        INSERT INTO "Attachments"
            ("Id", "CardId", "Kind", "Name", "FileName", "MimeType", "Bytes", "ObjectKey",
             "CreatedAt", "CreatedBy")
        SELECT gen_random_uuid(), {0}, 'File', 'shot.png', 'shot.png', 'image/png', 12,
               'cards/' || {0}::text || '/' || n, now(), {1}
        FROM generate_series(1, {2}::int) AS n
        """;

    public BoardCopyTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_brings_the_board_its_lists_and_every_card_that_is_not_archived()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14", BoardVisibility.Private);
        using (var described = await UpdateBoardAsync(
                   source.Id, "Sprint 14", "What we ship in March", BoardVisibility.Private))
        {
            Assert.Equal(HttpStatusCode.OK, described.StatusCode);
        }

        var todo = await CreateListAsync(source.Id, "To do", Color.Green);
        var away = await CreateListAsync(source.Id, "Old");
        var kept = await CreateCardAsync(todo.Id, "Write the brief");
        var archived = await CreateCardAsync(todo.Id, "Book the room");
        await CreateCardAsync(away.Id, "Chase the invoice");
        await SetCardArchivedAsync(archived.Id, true);
        await SetListArchivedAsync(away.Id, true);

        var queued = await CopyBoardAsync(source.Id, workspace.Id);
        Assert.Equal(CopyJobKind.Board, queued.Kind);
        Assert.Equal(CopyJobStatus.Queued, queued.Status);
        Assert.Equal(source.Id, queued.SourceId);
        Assert.Null(queued.ResultId);

        var done = await AwaitCopyJobAsync(queued.Id);
        Assert.Null(done.Errors);
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.FinishedAt);

        var copy = await ReadBoardAsync(done.ResultId!.Value);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Sprint 14", copy.Name);
        Assert.Equal("What we ship in March", copy.Description);
        Assert.Equal(BoardVisibility.Private, copy.Visibility);
        Assert.Equal(workspace.Id, copy.WorkspaceId);
        Assert.False(copy.IsArchived);
        Assert.Equal(ActorId, copy.CreatedBy);
        Assert.Null(copy.UpdatedAt);

        var lists = await ReadListsOfBoardAsync(copy.Id);
        var list = Assert.Single(lists);
        Assert.Equal("To do", list.Name);
        Assert.Equal(Color.Green, list.Color);
        Assert.Equal(todo.Position, list.Position);

        var card = Assert.Single(await ReadCardsOfListAsync(list.Id));
        Assert.Equal("Write the brief", card.Title);
        Assert.Equal(kept.Position, card.Position);
        Assert.NotEqual(kept.Id, card.Id);
    }

    [Fact]
    public async Task An_archived_board_copies_whole_and_lands_unarchived()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Q3 sprint");
        var doing = await CreateListAsync(source.Id, "Doing");
        var bug = await CreateLabelAsync(source.Id, "Bug", Color.Red);
        var card = await CreateCardAsync(doing.Id, "Write the brief", labelIds: [bug.Id]);
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it", isChecked: true);
        await CreateCheckItemAsync(steps.Id, "Send it");
        await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief", "The brief");
        await SetBoardArchivedAsync(source.Id, true);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id, name: "Q4 sprint");

        Assert.Equal("Q4 sprint", copy.Name);
        Assert.False(copy.IsArchived);
        Assert.True((await ReadBoardAsync(source.Id)).IsArchived);

        var list = Assert.Single(await ReadListsOfBoardAsync(copy.Id));
        var copied = Assert.Single(await ReadCardsOfListAsync(list.Id));
        var label = Assert.Single(await ReadLabelsOfBoardAsync(copy.Id));
        Assert.NotEqual(bug.Id, label.Id);
        Assert.Equal("Bug", label.Name);
        Assert.Equal(Color.Red, label.Color);
        Assert.Equal([label.Id], copied.LabelIds);
        Assert.Equal(2, copied.CheckItemCount);
        Assert.Equal(1, copied.CheckedItemCount);
        Assert.Equal(1, copied.AttachmentCount);
        Assert.Equal("Steps", Assert.Single(await ReadChecklistsAsync(copied.Id)).Name);
    }

    [Fact]
    public async Task Keeping_only_labels_gives_a_starter_board_with_its_lists_and_labels()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var todo = await CreateListAsync(source.Id, "To do");
        await CreateListAsync(source.Id, "Doing");
        await CreateListAsync(source.Id, "Done");
        var bug = await CreateLabelAsync(source.Id, "Bug", Color.Red);
        await CreateLabelAsync(source.Id, "Feature", Color.Green);
        await CreateCardAsync(todo.Id, "Write the brief", labelIds: [bug.Id]);
        await SetBoardArchivedAsync(source.Id, true);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id, keep: ["labels"]);

        var lists = await ReadListsOfBoardAsync(copy.Id);
        Assert.Equal(["To do", "Doing", "Done"], lists.ConvertAll(list => list.Name));
        Assert.All(lists, list => Assert.Empty(list.CardIds));
        var labels = await ReadLabelsOfBoardAsync(copy.Id);
        Assert.Equal(["Bug", "Feature"], labels.ConvertAll(label => label.Name));
        Assert.DoesNotContain(bug.Id, labels.ConvertAll(label => label.Id));
    }

    [Fact]
    public async Task Labels_no_card_uses_come_along_and_copied_cards_point_at_the_copies()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var bug = await CreateLabelAsync(source.Id, "Bug", Color.Red);
        await CreateLabelAsync(source.Id, null, Color.Green);
        await CreateCardAsync(doing.Id, "Write the brief", labelIds: [bug.Id]);
        await CreateCardAsync(doing.Id, "Book the room", labelIds: [bug.Id]);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id);

        var labels = await ReadLabelsOfBoardAsync(copy.Id);
        Assert.Equal(2, labels.Count);
        Assert.Equal(2, (await ReadLabelsOfBoardAsync(source.Id)).Count);
        var copiedBug = labels.Find(label => label.Name == "Bug")!;
        Assert.NotEqual(bug.Id, copiedBug.Id);

        var cards = await ReadCardsOfListAsync(Assert.Single(await ReadListsOfBoardAsync(copy.Id)).Id);
        Assert.Equal(2, cards.Count);
        Assert.All(cards, card => Assert.Equal([copiedBug.Id], card.LabelIds));
    }

    [Fact]
    public async Task Without_labels_the_new_board_has_none()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var bug = await CreateLabelAsync(source.Id, "Bug", Color.Red);
        await CreateCardAsync(doing.Id, "Write the brief", labelIds: [bug.Id]);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id, keep: ["cards"]);

        Assert.Empty(await ReadLabelsOfBoardAsync(copy.Id));
        var card = Assert.Single(await ReadCardsOfListAsync(
            Assert.Single(await ReadListsOfBoardAsync(copy.Id)).Id));
        Assert.Empty(card.LabelIds);
    }

    [Fact]
    public async Task A_kept_file_gets_its_own_object_under_the_new_card()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var card = await CreateCardAsync(doing.Id, "Write the brief");
        var uploaded = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, attachmentId: uploaded.Id);

        var copy = await CopiedBoardAsync(source.Id, workspace.Id);

        var copied = Assert.Single(await ReadCardsOfListAsync(
            Assert.Single(await ReadListsOfBoardAsync(copy.Id)).Id));
        var attachment = Assert.Single(await ReadAttachmentsAsync(copied.Id));
        Assert.NotEqual(uploaded.Id, attachment.Id);
        Assert.Equal(attachment.Id, copied.Cover!.AttachmentId);
        Assert.Equal(
            await DownloadAttachmentAsync(card.Id, uploaded.Id),
            await DownloadAttachmentAsync(copied.Id, attachment.Id));
        Assert.Empty(await FromDatabaseAsync(context => context.PendingObjectDeletes.ToListAsync()));
    }

    [Fact]
    public async Task A_copy_lands_in_the_workspace_the_body_names()
    {
        var from = await CreateWorkspaceAsync("Design");
        var to = await CreateWorkspaceAsync("Sales");
        var source = await CreateBoardAsync(from.Id, "Sprint 14");
        await CreateListAsync(source.Id, "Doing");

        var copy = await CopiedBoardAsync(source.Id, to.Id);

        Assert.Equal(to.Id, copy.WorkspaceId);
        Assert.Single(await ReadListsOfBoardAsync(copy.Id));
    }

    [Fact]
    public async Task The_202_points_at_the_job_route()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(source.Id, new { workspaceId = workspace.Id });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.ReadEnvelope<CopyJobDto>()).Data!;
        Assert.Equal(CopyJobPath(job.Id), response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task A_blank_name_takes_the_source_name_and_a_given_one_replaces_it()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        var missing = await CopiedBoardAsync(source.Id, workspace.Id);
        var blank = await CopiedBoardAsync(source.Id, workspace.Id, name: "   ");
        var named = await CopiedBoardAsync(source.Id, workspace.Id, name: "Sprint 15");

        Assert.Equal("Sprint 14", missing.Name);
        Assert.Equal("Sprint 14", blank.Name);
        Assert.Equal("Sprint 15", named.Name);
    }

    [Fact]
    public async Task A_name_over_the_limit_is_tooLong()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(source.Id, new
        {
            workspaceId = workspace.Id,
            name = new string('x', FieldLimits.BoardName + 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("name", error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("before")]
    [InlineData("after")]
    public async Task The_body_takes_no_placement(string field)
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(source.Id, new Dictionary<string, object>
        {
            ["workspaceId"] = workspace.Id,
            [field] = field == "position" ? "Top" : Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(field, error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Comments_is_an_unknown_keep_value()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(
            source.Id, new { workspaceId = workspace.Id, keep = new[] { "cards", "comments" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("keep[1]", error.Field);
        Assert.Equal(ErrorCodes.UnknownValue, error.Code);
    }

    [Fact]
    public async Task Checklists_or_attachments_without_cards_is_requiresCards_on_that_entry()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(
            source.Id,
            new { workspaceId = workspace.Id, keep = new[] { "labels", "checklists", "attachments" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await response.ReadErrors();
        Assert.Equal(["keep[1]", "keep[2]"], errors.ConvertAll(error => error.Field));
        Assert.All(errors, error => Assert.Equal(ErrorCodes.RequiresCards, error.Code));
    }

    [Fact]
    public async Task A_requiresCards_entry_comes_back_with_the_other_body_errors()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(source.Id, new
        {
            workspaceId = workspace.Id,
            name = new string('x', FieldLimits.BoardName + 1),
            keep = new[] { "checklists" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await response.ReadErrors();
        Assert.Contains(errors, error => error.Field == "name" && error.Code == ErrorCodes.TooLong);
        Assert.Contains(errors, error => error.Field == "keep[0]" && error.Code == ErrorCodes.RequiresCards);
    }

    [Fact]
    public async Task Checklists_and_attachments_are_fine_alongside_cards()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var card = await CreateCardAsync(doing.Id, "Write the brief");
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it");

        var copy = await CopiedBoardAsync(
            source.Id, workspace.Id, keep: ["cards", "checklists", "attachments"]);

        var copied = Assert.Single(await ReadCardsOfListAsync(
            Assert.Single(await ReadListsOfBoardAsync(copy.Id)).Id));
        Assert.Equal("Steps", Assert.Single(await ReadChecklistsAsync(copied.Id)).Name);
        Assert.Empty(copied.LabelIds);
    }

    [Fact]
    public async Task A_copy_needs_an_actor()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendWithoutActorAsync(
            HttpMethod.Post, BoardCopyPath(source.Id), new { workspaceId = workspace.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ActorFilter.HeaderName, error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task The_body_is_read_before_the_source_is_looked_up()
    {
        using var response = await SendBoardCopyAsync(Guid.NewGuid(), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("workspaceId", error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task An_unknown_source_is_404_before_the_destination_is_checked()
    {
        using var response = await SendBoardCopyAsync(Guid.NewGuid(), new { workspaceId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_destination_is_notFound_on_workspaceId()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");

        using var response = await SendBoardCopyAsync(source.Id, new { workspaceId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("workspaceId", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task A_board_over_the_card_cap_is_409_limitReached_unless_the_cards_stay_behind()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        await FromDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(
            BulkCards, doing.Id, ActorId, FieldLimits.CardsPerCopy + 1));

        using var refused = await SendBoardCopyAsync(source.Id, new { workspaceId = workspace.Id });
        using var allowed = await SendBoardCopyAsync(
            source.Id, new { workspaceId = workspace.Id, keep = new[] { "labels" } });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("5,000 cards", error.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    [Fact]
    public async Task A_board_over_the_file_cap_is_409_limitReached_unless_the_files_stay_behind()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        var doing = await CreateListAsync(source.Id, "Doing");
        var card = await CreateCardAsync(doing.Id, "Write the brief");
        await FromDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(
            BulkFiles, card.Id, ActorId, FieldLimits.FilesPerCopy + 1));

        using var refused = await SendBoardCopyAsync(source.Id, new { workspaceId = workspace.Id });
        using var allowed = await SendBoardCopyAsync(
            source.Id, new { workspaceId = workspace.Id, keep = new[] { "cards" } });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("5,000 files", error.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    [Fact]
    public async Task A_job_whose_source_is_gone_fails_with_notFound_and_no_field()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        var job = await QueueAsync(Guid.NewGuid(), new { workspaceId = workspace.Id });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        var error = Assert.Single(failed.Errors!);
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task A_job_fails_when_the_workspace_went_away_while_it_waited()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var gone = await CreateWorkspaceAsync("Sales");
        var source = await CreateBoardAsync(workspace.Id, "Sprint 14");
        await CreateListAsync(source.Id, "Doing");
        await DeleteWorkspaceAsync(gone.Id);

        var job = await QueueAsync(source.Id, new { workspaceId = gone.Id });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        var error = Assert.Single(failed.Errors!);
        Assert.Equal("workspaceId", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    private async Task<BoardDto> CopiedBoardAsync(
        Guid id, Guid workspaceId, string? name = null, IEnumerable<string>? keep = null)
    {
        var job = await AwaitCopyJobAsync((await CopyBoardAsync(id, workspaceId, name, keep)).Id);
        return await ReadBoardAsync(job.ResultId!.Value);
    }

    private async Task DeleteWorkspaceAsync(Guid id)
    {
        using var response = await DeleteAsync($"/api/v1/workspaces/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Guid> QueueAsync(Guid sourceId, object request)
    {
        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = CopyJobKind.Board,
                SourceId = sourceId,
                Request = JsonSerializer.SerializeToDocument(request),
                Status = CopyJobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await context.SaveChangesAsync();
        });

        return id;
    }
}
