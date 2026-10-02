using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ListCopyTests : ApiTests
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

    public ListCopyTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_copy_brings_the_list_and_every_card_that_is_not_archived()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing", Color.Green);
        var first = await CreateCardAsync(source.Id, "Write the brief");
        var away = await CreateCardAsync(source.Id, "Book the room");
        var last = await CreateCardAsync(source.Id, "Send the notes");
        await SetCardArchivedAsync(away.Id, true);

        var queued = await CopyListAsync(source.Id, to.Id);
        Assert.Equal(CopyJobStatus.Queued, queued.Status);
        Assert.Equal(CopyJobKind.List, queued.Kind);
        Assert.Equal(source.Id, queued.SourceId);
        Assert.Null(queued.ResultId);

        var done = await AwaitCopyJobAsync(queued.Id);

        var copy = await ReadListAsync(done.ResultId!.Value);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Doing", copy.Name);
        Assert.Equal(Color.Green, copy.Color);
        Assert.Equal(to.Id, copy.BoardId);
        Assert.False(copy.IsArchived);
        Assert.Equal(ActorId, copy.CreatedBy);
        Assert.Null(copy.UpdatedAt);

        var cards = await ReadCardsOfListAsync(copy.Id);
        Assert.Equal(["Write the brief", "Send the notes"], cards.ConvertAll(card => card.Title));
        Assert.Equal([first.Position, last.Position], cards.ConvertAll(card => card.Position));
        Assert.Equal(2, (await ReadCardsOfListAsync(source.Id)).Count);
    }

    [Fact]
    public async Task A_finished_job_carries_its_stamps_and_no_errors()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var done = await AwaitCopyJobAsync((await CopyListAsync(source.Id, board.Id)).Id);

        Assert.Equal(CopyJobStatus.Succeeded, done.Status);
        Assert.Null(done.Errors);
        Assert.Equal(ActorId, done.CreatedBy);
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.FinishedAt);
        Assert.True(done.StartedAt >= done.CreatedAt);
        Assert.True(done.FinishedAt >= done.StartedAt);
    }

    [Fact]
    public async Task The_202_points_at_the_job_route()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendListCopyAsync(source.Id, new { boardId = board.Id });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.ReadEnvelope<CopyJobDto>()).Data!;
        Assert.Equal(CopyJobPath(job.Id), response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task The_job_route_ignores_the_actor_header()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var job = await CopyListAsync(source.Id, board.Id);

        using var response = await GetAsync(CopyJobPath(job.Id), "not-a-user-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_job_is_404()
    {
        using var response = await GetAsync(CopyJobPath(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_copy_with_no_name_takes_the_source_name_with_no_prefix()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var copy = await CopiedListAsync(source.Id, board.Id);

        Assert.Equal("Doing", copy.Name);
    }

    [Fact]
    public async Task A_blank_name_takes_the_source_name_and_a_given_one_replaces_it()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var blank = await CopiedListAsync(source.Id, board.Id, name: "   ");
        var named = await CopiedListAsync(source.Id, board.Id, name: "Doing next");

        Assert.Equal("Doing", blank.Name);
        Assert.Equal("Doing next", named.Name);
    }

    [Fact]
    public async Task A_name_over_the_limit_is_tooLong()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendListCopyAsync(
            source.Id, new { boardId = board.Id, name = new string('x', FieldLimits.ListName + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("name", error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
    }

    [Fact]
    public async Task Comments_is_an_unknown_keep_value()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendListCopyAsync(
            source.Id, new { boardId = board.Id, keep = new[] { "labels", "comments" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("keep[1]", error.Field);
        Assert.Equal(ErrorCodes.UnknownValue, error.Code);
    }

    [Fact]
    public async Task Another_body_field_is_unknownField()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendListCopyAsync(source.Id, new { boardId = board.Id, color = "Green" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("color", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task A_copy_needs_an_actor()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendWithoutActorAsync(
            HttpMethod.Post, ListCopyPath(source.Id), new { boardId = board.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ActorFilter.HeaderName, error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task The_body_is_read_before_the_source_is_looked_up()
    {
        using var response = await SendListCopyAsync(Guid.NewGuid(), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.Required, error.Code);
    }

    [Fact]
    public async Task An_unknown_source_is_404_before_the_destination_is_checked()
    {
        using var response = await SendListCopyAsync(Guid.NewGuid(), new { boardId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_destination_is_notFound_on_boardId()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        using var response = await SendListCopyAsync(source.Id, new { boardId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task An_anchor_on_another_board_is_notSibling()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");

        using var response = await SendListCopyAsync(source.Id, new { boardId = to.Id, after = source.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task Anchors_that_are_not_neighbours_are_notAdjacent()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var first = await CreateListAsync(board.Id, "Doing");
        await CreateListAsync(board.Id, "Blocked");
        var third = await CreateListAsync(board.Id, "Done");

        using var response = await SendListCopyAsync(
            first.Id, new { boardId = board.Id, after = first.Id, before = third.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("before", error.Field);
        Assert.Equal(ErrorCodes.NotAdjacent, error.Code);
    }

    [Fact]
    public async Task An_archived_destination_is_409_on_boardId()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        await SetBoardArchivedAsync(to.Id, true);

        using var response = await SendListCopyAsync(source.Id, new { boardId = to.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
    }

    [Fact]
    public async Task A_bad_anchor_beats_the_archived_destination()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        await SetBoardArchivedAsync(to.Id, true);

        using var response = await SendListCopyAsync(
            source.Id, new { boardId = to.Id, after = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task An_archived_list_can_be_copied_and_lands_unarchived()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        await CreateCardAsync(source.Id, "Write the brief");
        await SetListArchivedAsync(source.Id, true);

        var copy = await CopiedListAsync(source.Id, board.Id);

        Assert.False(copy.IsArchived);
        Assert.Single(await ReadCardsOfListAsync(copy.Id));
    }

    [Fact]
    public async Task A_copy_lands_at_the_bottom_by_default_and_takes_an_anchor()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var first = await CreateListAsync(board.Id, "Doing");
        var last = await CreateListAsync(board.Id, "Done");

        var bottom = await CopiedListAsync(first.Id, board.Id);
        var anchored = await CopiedListAsync(first.Id, board.Id, after: first.Id);

        Assert.True(bottom.Position > last.Position);
        Assert.True(anchored.Position > first.Position);
        Assert.True(anchored.Position < last.Position);
    }

    [Fact]
    public async Task Cards_carry_their_labels_checklists_and_attachments()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var bug = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var card = await CreateCardAsync(source.Id, "Write the brief", labelIds: [bug.Id]);
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it", isChecked: true);
        await CreateCheckItemAsync(steps.Id, "Send it");
        await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief", "The brief");

        var copy = await CopiedListAsync(source.Id, board.Id);

        var copied = Assert.Single(await ReadCardsOfListAsync(copy.Id));
        Assert.NotEqual(card.Id, copied.Id);
        Assert.Equal([bug.Id], copied.LabelIds);
        Assert.Equal(2, copied.CheckItemCount);
        Assert.Equal(1, copied.CheckedItemCount);
        Assert.Equal(1, copied.AttachmentCount);
        var checklist = Assert.Single(await ReadChecklistsAsync(copied.Id));
        Assert.Equal("Steps", checklist.Name);
        Assert.Equal(steps.Position, checklist.Position);
    }

    [Fact]
    public async Task An_empty_keep_brings_nothing_but_the_cards_own_fields()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var bug = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var card = await CreateCardAsync(source.Id, "Write the brief", labelIds: [bug.Id]);
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await CreateCheckItemAsync(steps.Id, "Draft it");
        await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief", "The brief");

        var copy = await CopiedListAsync(source.Id, board.Id, keep: []);

        var copied = Assert.Single(await ReadCardsOfListAsync(copy.Id));
        Assert.Empty(copied.LabelIds);
        Assert.Empty(await ReadChecklistsAsync(copied.Id));
        Assert.Empty(await ReadAttachmentsAsync(copied.Id));
    }

    [Fact]
    public async Task A_kept_file_gets_its_own_object_under_the_new_card()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(source.Id, "Write the brief");
        var uploaded = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        await SetCoverAsync(card.Id, attachmentId: uploaded.Id);

        var copy = await CopiedListAsync(source.Id, board.Id);

        var copied = Assert.Single(await ReadCardsOfListAsync(copy.Id));
        var attachment = Assert.Single(await ReadAttachmentsAsync(copied.Id));
        Assert.NotEqual(uploaded.Id, attachment.Id);
        Assert.Equal(attachment.Id, copied.Cover!.AttachmentId);
        Assert.Equal(
            await DownloadAttachmentAsync(card.Id, uploaded.Id),
            await DownloadAttachmentAsync(copied.Id, attachment.Id));
        Assert.Empty(await FromDatabaseAsync(context => context.PendingObjectDeletes.ToListAsync()));
    }

    [Fact]
    public async Task Labels_keep_their_ids_on_the_same_board()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var bug = await CreateLabelAsync(board.Id, "Bug", Color.Red);
        var green = await CreateLabelAsync(board.Id, null, Color.Green);
        await CreateCardAsync(source.Id, "Write the brief", labelIds: [bug.Id, green.Id]);

        var copy = await CopiedListAsync(source.Id, board.Id);

        var copied = Assert.Single(await ReadCardsOfListAsync(copy.Id));
        Assert.Equal(2, copied.LabelIds.Count);
        Assert.Contains(bug.Id, copied.LabelIds);
        Assert.Contains(green.Id, copied.LabelIds);
    }

    [Fact]
    public async Task Cards_that_need_the_same_new_label_on_another_board_share_one()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        var red = await CreateLabelAsync(from.Id, "Bug", Color.Red);
        var green = await CreateLabelAsync(from.Id, null, Color.Green);
        var blue = await CreateLabelAsync(to.Id, "bug", Color.Blue);
        await CreateCardAsync(source.Id, "Write the brief", labelIds: [red.Id, green.Id]);
        await CreateCardAsync(source.Id, "Book the room", labelIds: [green.Id]);

        var copy = await CopiedListAsync(source.Id, to.Id);

        var cards = await ReadCardsOfListAsync(copy.Id);
        var brief = cards.Find(card => card.Title == "Write the brief")!;
        var room = cards.Find(card => card.Title == "Book the room")!;
        Assert.Contains(blue.Id, brief.LabelIds);
        Assert.DoesNotContain(red.Id, brief.LabelIds);
        var carried = Assert.Single(room.LabelIds);
        Assert.NotEqual(green.Id, carried);
        Assert.Contains(carried, brief.LabelIds);
        Assert.Equal(2, (await ReadLabelsAsync(to.Id)).Count);
    }

    [Fact]
    public async Task A_list_over_the_card_cap_is_409_limitReached()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        await FromDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(
            BulkCards, source.Id, ActorId, FieldLimits.CardsPerCopy + 1));

        using var response = await SendListCopyAsync(source.Id, new { boardId = board.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("5,000 cards", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_over_the_file_cap_is_409_limitReached_unless_the_files_stay_behind()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var card = await CreateCardAsync(source.Id, "Write the brief");
        await FromDatabaseAsync(context => context.Database.ExecuteSqlRawAsync(
            BulkFiles, card.Id, ActorId, FieldLimits.FilesPerCopy + 1));

        using var refused = await SendListCopyAsync(source.Id, new { boardId = board.Id });
        using var allowed = await SendListCopyAsync(
            source.Id, new { boardId = board.Id, keep = new[] { "labels" } });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = Assert.Single(await refused.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("5,000 files", error.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    [Fact]
    public async Task A_job_fails_when_the_board_was_archived_while_it_waited()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var from = await CreateBoardAsync(workspace.Id, "Roadmap");
        var to = await CreateBoardAsync(workspace.Id, "Backlog");
        var source = await CreateListAsync(from.Id, "Doing");
        await CreateCardAsync(source.Id, "Write the brief");
        await SetBoardArchivedAsync(to.Id, true);

        var job = await QueueAsync(source.Id, new { boardId = to.Id });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        Assert.Equal(1, await AttemptsAsync(job));
        var error = Assert.Single(failed.Errors!);
        Assert.Equal("boardId", error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Empty(await ReadListsAsync(to.Id));
    }

    [Fact]
    public async Task A_job_whose_source_is_gone_fails_with_notFound_and_no_field()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");

        var job = await QueueAsync(Guid.NewGuid(), new { boardId = board.Id });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        var error = Assert.Single(failed.Errors!);
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task A_running_job_no_worker_holds_is_taken_over_as_a_new_attempt()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        await CreateCardAsync(source.Id, "Write the brief");

        var job = await QueueAsync(
            source.Id, new { boardId = board.Id }, CopyJobStatus.Running, attempts: 1);
        var done = await AwaitCopyJobAsync(job);

        Assert.Equal(2, await AttemptsAsync(job));
        Assert.Single(await ReadCardsOfListAsync(done.ResultId!.Value));
    }

    [Fact]
    public async Task A_job_that_used_its_three_attempts_fails_with_internalError()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var job = await QueueAsync(
            source.Id, new { boardId = board.Id }, CopyJobStatus.Running, attempts: 3);
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        Assert.Null(failed.ResultId);
        Assert.Equal(3, await AttemptsAsync(job));
        var error = Assert.Single(failed.Errors!);
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.InternalError, error.Code);
    }

    [Fact]
    public async Task A_copy_takes_a_position_through_the_job()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");

        var top = await AwaitCopyJobAsync(
            (await CopyListAsync(source.Id, board.Id, position: "Top")).Id);
        var placed = await AwaitCopyJobAsync(
            (await CopyListAsync(source.Id, board.Id, position: 500)).Id);

        Assert.True((await ReadListAsync(top.ResultId!.Value)).Position < source.Position);
        Assert.Equal(500, (await ReadListAsync(placed.ResultId!.Value)).Position);
    }

    [Fact]
    public async Task A_job_is_swept_seven_days_after_it_finished()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var source = await CreateListAsync(board.Id, "Doing");
        var old = await AwaitCopyJobAsync((await CopyListAsync(source.Id, board.Id)).Id);
        var recent = await AwaitCopyJobAsync((await CopyListAsync(source.Id, board.Id)).Id);
        await FinishedAtAsync(old.Id, DateTime.UtcNow.AddDays(-8));

        var swept = await FromServicesAsync(services =>
            services.GetRequiredService<CopyJobSweep>().RunAsync(CancellationToken.None));

        Assert.Equal(1, swept);
        using var gone = await GetAsync(CopyJobPath(old.Id));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using var kept = await GetAsync(CopyJobPath(recent.Id));
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
    }

    private async Task<ListDto> CopiedListAsync(
        Guid id,
        Guid boardId,
        string? name = null,
        IEnumerable<string>? keep = null,
        Guid? after = null)
    {
        var job = await AwaitCopyJobAsync((await CopyListAsync(id, boardId, name, keep, after: after)).Id);
        return await ReadListAsync(job.ResultId!.Value);
    }

    private async Task<Guid> QueueAsync(
        Guid sourceId, object request, CopyJobStatus status = CopyJobStatus.Queued, int attempts = 0)
    {
        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = CopyJobKind.List,
                SourceId = sourceId,
                Request = JsonSerializer.SerializeToDocument(request),
                Status = status,
                Attempts = attempts,
                StartedAt = attempts == 0 ? null : DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            });

            return await context.SaveChangesAsync();
        });

        return id;
    }

    private Task<int> AttemptsAsync(Guid id) =>
        FromDatabaseAsync(context => context.CopyJobs
            .Where(job => job.Id == id)
            .Select(job => job.Attempts)
            .FirstAsync());

    private Task<int> FinishedAtAsync(Guid id, DateTime finishedAt) =>
        FromDatabaseAsync(context => context.CopyJobs
            .Where(job => job.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(job => job.FinishedAt, (DateTime?)finishedAt)));

    private async Task<List<ListDto>> ReadListsAsync(Guid boardId)
    {
        using var response = await GetAsync($"/api/v1/lists?boardId={boardId}&archived=Include");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ListDto>()).Entries;
    }

    private async Task<List<LabelDto>> ReadLabelsAsync(Guid boardId)
    {
        using var response = await GetAsync($"/api/v1/labels?boardId={boardId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<LabelDto>()).Entries;
    }
}
