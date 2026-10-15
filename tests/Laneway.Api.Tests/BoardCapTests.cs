using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardCapTests : ApiTests
{
    private const int Cards = FieldLimits.OpenCardsPerBoard;
    private const int Lists = FieldLimits.OpenListsPerBoard;
    private const int Labels = FieldLimits.LabelsPerBoard;

    public BoardCapTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_card_create_fits_one_below_the_cap_and_is_409_at_it()
    {
        var (board, list) = await NewBoardAsync();
        await SeedCardsAsync(list.Id, Cards - 1);

        using var fits = await PostCardAsync(list.Id, "Last one");
        using var over = await PostCardAsync(list.Id, "One too many");

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        await AssertCappedAsync(over, "at most 5,000 open cards. This one has 5,000, and this change adds 1.");
        Assert.Equal(Cards, await CardCountAsync(list.Id));
        Assert.Single(await EntriesAsync(board.Id, "createCard"));
    }

    [Fact]
    public async Task Archived_cards_and_cards_in_archived_lists_do_not_count()
    {
        var (_, list) = await NewBoardAsync();
        var shelved = await CreateListAsync(list.BoardId, "Shelved");
        await SetListArchivedAsync(shelved.Id, true);
        await SeedCardsAsync(list.Id, Cards - 1);
        await SeedCardsAsync(list.Id, 20, archived: true);
        await SeedCardsAsync(shelved.Id, 20);

        using var fits = await PostCardAsync(list.Id, "Last one");
        using var over = await PostCardAsync(list.Id, "One too many");

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        await AssertCappedAsync(over, "5,000 open cards");
    }

    [Fact]
    public async Task A_list_create_fits_one_below_the_cap_and_archived_lists_do_not_count()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        await SeedListsAsync(board.Id, Lists - 1);
        await SeedListsAsync(board.Id, 20, archived: true);

        using var fits = await PostAsync(ListsPath(board.Id), new { name = "Last one" });
        using var over = await PostAsync(ListsPath(board.Id), new { name = "One too many" });

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        await AssertCappedAsync(over, "at most 500 open lists. This one has 500, and this change adds 1.");
        Assert.Single(await EntriesAsync(board.Id, "createList"));
    }

    [Fact]
    public async Task A_label_create_fits_one_below_the_cap_and_is_409_at_it()
    {
        var (board, _) = await NewBoardAsync();
        await SeedLabelsAsync(board.Id, Labels - 1);

        using var fits = await PostAsync(LabelsPath(board.Id), new { name = "Last one" });
        using var over = await PostAsync(LabelsPath(board.Id), new { name = "One too many" });

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        await AssertCappedAsync(over, "at most 1,000 labels. This one has 1,000, and this change adds 1.");
        Assert.Single(await EntriesAsync(board.Id, "createLabel"));
    }

    [Fact]
    public async Task A_card_move_to_another_board_counts_the_card()
    {
        var (_, from) = await NewBoardAsync();
        var (full, to) = await NewBoardAsync();
        var first = await CreateCardAsync(from.Id, "Write the brief");
        var second = await CreateCardAsync(from.Id, "Book the studio");
        await SeedCardsAsync(to.Id, Cards - 1);

        using var fits = await MoveCardAsync(first.Id, to.Id);
        using var over = await MoveCardAsync(second.Id, to.Id);

        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        await AssertCappedAsync(over, "5,000 open cards");
        Assert.Equal(from.Id, (await ReadCardAsync(second.Id)).ListId);
        Assert.Single(await EntriesAsync(full.Id, "moveCard"));
    }

    [Fact]
    public async Task A_card_move_inside_a_full_board_checks_nothing()
    {
        var (_, list) = await NewBoardAsync();
        var other = await CreateListAsync(list.BoardId, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        await SeedCardsAsync(list.Id, Cards - 1);

        using var moved = await MoveCardAsync(card.Id, other.Id);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    [Fact]
    public async Task A_card_move_counts_the_labels_it_creates_on_the_destination()
    {
        var (fromBoard, from) = await NewBoardAsync();
        var (toBoard, to) = await NewBoardAsync();
        await SeedLabelsAsync(toBoard.Id, Labels - 1);
        var urgent = await CreateLabelAsync(fromBoard.Id, "Urgent");
        var blocked = await CreateLabelAsync(fromBoard.Id, "Blocked");
        var matching = await CreateLabelAsync(fromBoard.Id, "Seeded label 1");
        var refused = await CreateCardAsync(from.Id, "Write the brief", labelIds: [urgent.Id, blocked.Id]);
        var matched = await CreateCardAsync(from.Id, "Book the studio", labelIds: [matching.Id]);

        using var over = await MoveCardAsync(refused.Id, to.Id);
        using var fits = await MoveCardAsync(matched.Id, to.Id);

        await AssertCappedAsync(over, "at most 1,000 labels. This one has 999, and this change adds 2.");
        Assert.Equal(from.Id, (await ReadCardAsync(refused.Id)).ListId);
        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        Assert.Equal(Labels - 1, await LabelCountAsync(toBoard.Id));
    }

    [Fact]
    public async Task A_list_move_to_another_board_counts_its_open_cards()
    {
        var (_, from) = await NewBoardAsync();
        var (full, to) = await NewBoardAsync();
        var small = await CreateListAsync(from.BoardId, "Small");
        await CreateCardAsync(from.Id, "Write the brief");
        await CreateCardAsync(from.Id, "Book the studio");
        await SeedCardsAsync(from.Id, 5, archived: true);
        await CreateCardAsync(small.Id, "Send the invoice");
        await SeedCardsAsync(to.Id, Cards - 2);

        using var fits = await MoveListAsync(from.Id, full.Id);
        using var over = await MoveListAsync(small.Id, full.Id);

        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        await AssertCappedAsync(over, "at most 5,000 open cards. This one has 5,000, and this change adds 1.");
        Assert.Equal(from.BoardId, (await ReadListAsync(small.Id)).BoardId);
        Assert.Single(await EntriesAsync(full.Id, "moveList"));
    }

    [Fact]
    public async Task A_list_move_counts_the_labels_its_cards_create_on_the_destination()
    {
        var (fromBoard, from) = await NewBoardAsync();
        var (toBoard, _) = await NewBoardAsync();
        await SeedLabelsAsync(toBoard.Id, Labels - 1);
        var urgent = await CreateLabelAsync(fromBoard.Id, "Urgent");
        var blocked = await CreateLabelAsync(fromBoard.Id, "Blocked");
        await CreateCardAsync(from.Id, "Write the brief", labelIds: [urgent.Id]);
        await CreateCardAsync(from.Id, "Book the studio", labelIds: [urgent.Id, blocked.Id]);

        using var over = await MoveListAsync(from.Id, toBoard.Id);

        await AssertCappedAsync(over, "at most 1,000 labels. This one has 999, and this change adds 2.");
        Assert.Equal(fromBoard.Id, (await ReadListAsync(from.Id)).BoardId);
        Assert.Equal(Labels - 1, await LabelCountAsync(toBoard.Id));
    }

    [Fact]
    public async Task A_list_move_to_another_board_counts_the_list()
    {
        var (_, first) = await NewBoardAsync();
        var second = await CreateListAsync(first.BoardId, "Doing");
        var (full, _) = await NewBoardAsync();
        await SeedListsAsync(full.Id, Lists - 2);

        using var fits = await MoveListAsync(first.Id, full.Id);
        using var over = await MoveListAsync(second.Id, full.Id);

        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        await AssertCappedAsync(over, "500 open lists");
    }

    [Fact]
    public async Task A_card_copy_counts_the_copy_even_inside_one_board()
    {
        var (board, list) = await NewBoardAsync();
        var card = await CreateCardAsync(list.Id, "Write the brief");
        await SeedCardsAsync(list.Id, Cards - 2);

        using var fits = await SendCardCopyAsync(card.Id, new { listId = list.Id });
        using var over = await SendCardCopyAsync(card.Id, new { listId = list.Id });

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        await AssertCappedAsync(over, "5,000 open cards");
        Assert.Single(await EntriesAsync(board.Id, "copyCard"));
    }

    [Fact]
    public async Task A_card_copy_counts_the_labels_it_creates_on_the_destination()
    {
        var (fromBoard, from) = await NewBoardAsync();
        var (toBoard, to) = await NewBoardAsync();
        await SeedLabelsAsync(toBoard.Id, Labels);
        var urgent = await CreateLabelAsync(fromBoard.Id, "Urgent");
        var card = await CreateCardAsync(from.Id, "Write the brief", labelIds: [urgent.Id]);

        using var over = await SendCardCopyAsync(card.Id, new { listId = to.Id });
        using var bare = await SendCardCopyAsync(card.Id, new { listId = to.Id, keep = Array.Empty<string>() });

        await AssertCappedAsync(over, "1,000 labels");
        Assert.Equal(HttpStatusCode.Created, bare.StatusCode);
        Assert.Equal(Labels, await LabelCountAsync(toBoard.Id));
    }

    [Fact]
    public async Task A_list_copy_is_409_before_the_job_when_the_board_is_full()
    {
        var (_, source) = await NewBoardAsync();
        var (full, to) = await NewBoardAsync();
        var small = await CreateListAsync(source.BoardId, "Small");
        await CreateCardAsync(source.Id, "Write the brief");
        await CreateCardAsync(source.Id, "Book the studio");
        await CreateCardAsync(small.Id, "Send the invoice");
        await SeedCardsAsync(to.Id, Cards - 1);

        using var over = await SendListCopyAsync(source.Id, new { boardId = full.Id });
        var fits = await AwaitCopyJobAsync((await CopyListAsync(small.Id, full.Id)).Id);

        await AssertCappedAsync(over, "at most 5,000 open cards. This one has 4,999, and this change adds 2.");
        Assert.NotNull(fits.ResultId);
    }

    [Fact]
    public async Task A_list_copy_job_checks_again_and_fails_with_limitReached()
    {
        var (_, source) = await NewBoardAsync();
        var (full, to) = await NewBoardAsync();
        await CreateCardAsync(source.Id, "Write the brief");
        await SeedCardsAsync(to.Id, Cards);

        var job = await QueueAsync(CopyJobKind.List, source.Id, new { boardId = full.Id });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        var error = Assert.Single(failed.Errors!);
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("5,000 open cards", error.Message, StringComparison.Ordinal);
        Assert.Single(await ReadListsOfBoardAsync(full.Id));
        Assert.Empty(await EntriesAsync(full.Id, "copyList"));
    }

    [Fact]
    public async Task A_board_copy_is_409_before_the_job_when_the_copy_would_be_over_a_cap()
    {
        var (board, _) = await NewBoardAsync();
        var workspaceId = await WorkspaceOfAsync(board);
        await SeedListsAsync(board.Id, Lists - 1);

        var fits = await AwaitCopyJobAsync((await CopyBoardAsync(board.Id, workspaceId)).Id);
        await SeedListsAsync(board.Id, 1);
        using var over = await SendBoardCopyAsync(board.Id, new { workspaceId });

        Assert.NotNull(fits.ResultId);
        await AssertCappedAsync(over, "at most 500 open lists. This one has 0, and this change adds 501.");
    }

    [Fact]
    public async Task A_board_copy_job_checks_again_and_fails_with_limitReached()
    {
        var (board, _) = await NewBoardAsync();
        var workspaceId = await WorkspaceOfAsync(board);
        await SeedListsAsync(board.Id, Lists);

        var job = await QueueAsync(CopyJobKind.Board, board.Id, new { workspaceId });
        var failed = await AwaitCopyJobAsync(job, CopyJobStatus.Failed);

        var error = Assert.Single(failed.Errors!);
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("500 open lists", error.Message, StringComparison.Ordinal);
        using var boards = await GetAsync(BoardsPath(workspaceId));
        Assert.Equal(1, (await boards.ReadPage<BoardDto>()).Pagination.TotalCount);
    }

    [Fact]
    public async Task A_card_unarchive_counts_the_card()
    {
        var (board, list) = await NewBoardAsync();
        await SeedCardsAsync(list.Id, Cards - 1);
        var shelved = await CreateCardAsync(list.Id, "Write the brief");
        await SetCardArchivedAsync(shelved.Id, true);
        var last = await CreateCardAsync(list.Id, "Book the studio");

        using var over = await PutAsync($"/api/v1/cards/{shelved.Id}/archived", new { value = false });
        await SetCardArchivedAsync(last.Id, true);
        using var fits = await PutAsync($"/api/v1/cards/{shelved.Id}/archived", new { value = false });

        await AssertCappedAsync(over, "5,000 open cards");
        Assert.Equal(HttpStatusCode.NoContent, fits.StatusCode);
        Assert.Single(await EntriesAsync(board.Id, "restoreCard"));
    }

    [Fact]
    public async Task A_list_unarchive_counts_the_list_and_its_open_cards()
    {
        var (board, list) = await NewBoardAsync();
        var shelved = await CreateListAsync(board.Id, "Shelved");
        await CreateCardAsync(shelved.Id, "Write the brief");
        await SeedCardsAsync(shelved.Id, 5, archived: true);
        await SetListArchivedAsync(shelved.Id, true);
        await SeedCardsAsync(list.Id, Cards - 1);
        var last = await CreateCardAsync(list.Id, "Book the studio");

        using var over = await PutAsync($"/api/v1/lists/{shelved.Id}/archived", new { value = false });
        await SetCardArchivedAsync(last.Id, true);
        using var fits = await PutAsync($"/api/v1/lists/{shelved.Id}/archived", new { value = false });

        await AssertCappedAsync(over, "at most 5,000 open cards. This one has 5,000, and this change adds 1.");
        Assert.Equal(HttpStatusCode.NoContent, fits.StatusCode);
        Assert.Single(await EntriesAsync(board.Id, "restoreList"));
    }

    [Fact]
    public async Task A_list_unarchive_is_409_when_the_board_has_500_open_lists()
    {
        var (board, list) = await NewBoardAsync();
        await SetListArchivedAsync(list.Id, true);
        await SeedListsAsync(board.Id, Lists);

        using var over = await PutAsync($"/api/v1/lists/{list.Id}/archived", new { value = false });

        await AssertCappedAsync(over, "500 open lists");
        Assert.True((await ReadListAsync(list.Id)).IsArchived);
    }

    [Fact]
    public async Task More_than_100_labelIds_is_400()
    {
        var (_, list) = await NewBoardAsync();
        var card = await CreateCardAsync(list.Id, "Write the brief");
        var labelIds = Enumerable.Range(0, FieldLimits.LabelsPerCard + 1).Select(_ => Guid.NewGuid()).ToList();

        using var create = await PostCardAsync(list.Id, "Book the studio", labelIds);
        using var update = await UpdateCardAsync(card.Id, "Write the brief", labelIds: labelIds);

        foreach (var response in new[] { create, update })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = Assert.Single(await response.ReadErrors());
            Assert.Equal("labelIds", error.Field);
            Assert.Equal(ErrorCodes.TooLong, error.Code);
        }
    }

    [Fact]
    public async Task Adding_a_101st_label_to_a_card_is_409()
    {
        var (board, list) = await NewBoardAsync();
        var labelIds = await SeedLabelsAsync(board.Id, FieldLimits.LabelsPerCard + 1);
        var card = await CreateCardAsync(list.Id, "Write the brief");
        using var full = await UpdateCardAsync(card.Id, "Write the brief", labelIds: labelIds[..^1]);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);

        using var over = await PostAsync($"/api/v1/cards/{card.Id}/labels/{labelIds[^1]}", new { });
        using var already = await PostAsync($"/api/v1/cards/{card.Id}/labels/{labelIds[0]}", new { });

        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        var error = Assert.Single(await over.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains("at most 100 labels. This one has 100.", error.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, already.StatusCode);
        Assert.Equal(FieldLimits.LabelsPerCard, (await ReadCardAsync(card.Id)).LabelIds.Count);
        Assert.Empty(await EntriesAsync(board.Id, "addLabelToCard"));
    }

    [Fact]
    public async Task Two_card_creates_in_different_lists_cannot_both_take_the_last_place()
    {
        var (board, first) = await NewBoardAsync();
        var second = await CreateListAsync(board.Id, "Doing");
        await SeedCardsAsync(first.Id, Cards - 1);

        await using var holder = Services.CreateAsyncScope();
        var database = holder.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.TryLockBoardAsync(board.Id);

        var one = PostCardAsync(first.Id, "Write the brief");
        var two = PostCardAsync(second.Id, "Book the studio");
        await WaitForLockWaitAsync();
        await transaction.CommitAsync();
        using var a = await one;
        using var b = await two;

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            new[] { a.StatusCode, b.StatusCode }.Order());
        Assert.Equal(Cards, await CardCountAsync(first.Id) + await CardCountAsync(second.Id));
    }

    private async Task<(BoardDto Board, ListDto List)> NewBoardAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        return (board, await CreateListAsync(board.Id, "To do"));
    }

    private Task<HttpResponseMessage> PostCardAsync(Guid listId, string title, List<Guid>? labelIds = null) =>
        PostAsync(CardsPath(listId), new { title, labelIds, isDueComplete = false });

    private async Task<Guid> WorkspaceOfAsync(BoardDto board) => (await ReadBoardAsync(board.Id)).WorkspaceId;

    private static async Task AssertCappedAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private async Task<int> CardCountAsync(Guid listId) => (await ReadCardsAsync(listId, "pageSize=1")).Pagination.TotalCount;

    private async Task<int> LabelCountAsync(Guid boardId)
    {
        using var response = await GetAsync($"{LabelsPath(boardId)}?pageSize=1");
        return (await response.ReadPage<LabelDto>()).Pagination.TotalCount;
    }

    private async Task<List<ActivityEntryDto>> EntriesAsync(Guid boardId, string type) =>
        (await ActivityAsync($"boardId={boardId}&type={type}")).Entries;

    private async Task<Guid> QueueAsync(CopyJobKind kind, Guid sourceId, object request)
    {
        var id = Guid.NewGuid();
        await FromDatabaseAsync(async context =>
        {
            context.CopyJobs.Add(new CopyJob
            {
                Id = id,
                Kind = kind,
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
