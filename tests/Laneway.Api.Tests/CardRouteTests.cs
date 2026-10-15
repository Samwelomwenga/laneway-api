using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CardRouteTests : ApiTests
{
    private static readonly DateTime Due = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public CardRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_list_lists_only_its_own_cards()
    {
        var list = await NewListAsync();
        var other = await CreateListAsync(list.BoardId, "Doing");
        var card = await CreateCardAsync(list.Id, "Write the brief");
        await CreateCardAsync(other.Id, "Book the studio");

        var cards = await CardsAsync(list.Id);

        Assert.Equal(card.Id, Assert.Single(cards).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_searchTerm_and_archived()
    {
        var list = await NewListAsync();
        var brief = await CreateCardAsync(list.Id, "Write the brief");
        var studio = await CreateCardAsync(list.Id, "Book the studio");
        var archived = await CreateCardAsync(list.Id, "Send the invoice");
        await SetCardArchivedAsync(archived.Id, true);

        Assert.Equal(brief.Id, Assert.Single(await CardsAsync(list.Id, "searchTerm=brief")).Id);
        Assert.Equal([brief.Id, studio.Id], (await CardsAsync(list.Id)).Select(c => c.Id));
        Assert.Equal(archived.Id, Assert.Single(await CardsAsync(list.Id, "archived=only")).Id);
        Assert.Equal([brief.Id, studio.Id, archived.Id], (await CardsAsync(list.Id, "archived=include")).Select(c => c.Id));
    }

    [Fact]
    public async Task The_collection_filters_by_due_and_start_dates()
    {
        var list = await NewListAsync();
        var early = await PostCardAsync(list.Id, "Write the brief", Due, Due.AddDays(-3));
        var late = await PostCardAsync(list.Id, "Book the studio", Due.AddDays(2), Due.AddDays(1));
        await CreateCardAsync(list.Id, "Someday");

        Assert.Equal(early.Id, Assert.Single(await CardsAsync(list.Id, "dueDate=2026-10-01")).Id);
        Assert.Equal(early.Id, Assert.Single(await CardsAsync(list.Id, "dueBefore=2026-10-02T00:00:00Z")).Id);
        Assert.Equal(late.Id, Assert.Single(await CardsAsync(list.Id, "startFrom=2026-10-01T00:00:00Z")).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_isDueComplete()
    {
        var list = await NewListAsync();
        var done = await CreateCardAsync(list.Id, "Write the brief", Due, isDueComplete: true);
        var open = await CreateCardAsync(list.Id, "Book the studio", Due);
        await CreateCardAsync(list.Id, "Someday");

        Assert.Equal(done.Id, Assert.Single(await CardsAsync(list.Id, "isDueComplete=true")).Id);
        Assert.Equal(open.Id, Assert.Single(await CardsAsync(list.Id, "isDueComplete=false")).Id);
    }

    [Fact]
    public async Task A_create_puts_the_card_in_the_path_list()
    {
        var list = await NewListAsync();
        var label = await CreateLabelAsync(list.BoardId, "Urgent", Color.Red);
        var last = await CreateCardAsync(list.Id, "Send the invoice");

        using var response = await PostAsync(CardsPath(list.Id), new
        {
            title = "Write the brief",
            description = "Two pages, no more.",
            dueDate = Due,
            isDueComplete = false,
            startDate = Due.AddDays(-2),
            dueReminderMinutes = 60,
            labelIds = new[] { label.Id },
            before = last.Id
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = (await response.ReadEnvelope<CardDto>()).Data!;
        Assert.Equal(list.Id, card.ListId);
        Assert.Equal("Two pages, no more.", card.Description);
        Assert.Equal(Due.AddDays(-2), card.StartDate);
        Assert.Equal(60, card.DueReminderMinutes);
        Assert.Equal([label.Id], card.LabelIds);
        Assert.Equal($"/api/v1/cards/{card.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal([card.Id, last.Id], (await CardsAsync(list.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task A_label_from_another_board_is_notOnBoard()
    {
        var list = await NewListAsync();
        var otherBoard = await CreateBoardAsync((await CreateWorkspaceAsync("Sales")).Id, "Leads");
        var label = await CreateLabelAsync(list.BoardId, "Urgent", Color.Red);
        var elsewhere = await CreateLabelAsync(otherBoard.Id, "Hot", Color.Orange);

        using var response = await PostAsync(CardsPath(list.Id), new
        {
            title = "Write the brief",
            isDueComplete = false,
            labelIds = new[] { label.Id, elsewhere.Id }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("labelIds[1]", error.Field);
        Assert.Equal(ErrorCodes.NotOnBoard, error.Code);
    }

    [Fact]
    public async Task An_anchor_in_another_list_is_notSibling()
    {
        var list = await NewListAsync();
        var other = await CreateListAsync(list.BoardId, "Doing");
        var elsewhere = await CreateCardAsync(other.Id, "Book the studio");

        using var response = await PostAsync(CardsPath(list.Id), new
        {
            title = "Write the brief",
            isDueComplete = false,
            after = elsewhere.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task An_unknown_list_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(CardsPath(missing));
        using var create = await PostAsync(CardsPath(missing), new { title = "Write the brief", isDueComplete = false });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("List not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task ListId_in_the_query_is_unknownField()
    {
        var list = await NewListAsync();

        using var response = await GetAsync($"{CardsPath(list.Id)}?listId={list.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("listId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task ListId_in_the_create_body_is_unknownField()
    {
        var list = await NewListAsync();

        using var response = await PostAsync(CardsPath(list.Id), new
        {
            title = "Write the brief",
            isDueComplete = false,
            listId = list.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("listId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_list_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{CardsPath(missing)}?archived=Nope&dueBefore=soon");
        using var create = await PostAsync(CardsPath(missing), new { isDueComplete = false, position = "middle" });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["archived", "dueBefore"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(["position", "title"], (await create.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_archived_list_still_lists_its_cards()
    {
        var list = await NewListAsync();
        var brief = await CreateCardAsync(list.Id, "Write the brief");
        var invoice = await CreateCardAsync(list.Id, "Send the invoice");
        await SetCardArchivedAsync(invoice.Id, true);
        await SetListArchivedAsync(list.Id, true);

        Assert.Equal(brief.Id, Assert.Single(await CardsAsync(list.Id)).Id);
        Assert.Equal(invoice.Id, Assert.Single(await CardsAsync(list.Id, "archived=only")).Id);
    }

    [Fact]
    public async Task A_list_on_an_archived_board_still_lists_its_cards_and_refuses_a_create()
    {
        var list = await NewListAsync();
        var brief = await CreateCardAsync(list.Id, "Write the brief");
        await SetBoardArchivedAsync(list.BoardId, true);

        using var response = await PostAsync(CardsPath(list.Id), new { title = "Book the studio", isDueComplete = false });

        Assert.Equal(brief.Id, Assert.Single(await CardsAsync(list.Id)).Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
    }

    [Fact]
    public async Task A_create_in_an_archived_list_is_409_archived_with_no_field()
    {
        var list = await NewListAsync();
        await SetListArchivedAsync(list.Id, true);

        using var response = await PostAsync(CardsPath(list.Id), new { title = "Write the brief", isDueComplete = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Empty(await CardsAsync(list.Id, "archived=include"));
    }

    [Fact]
    public async Task The_flat_card_collection_routes_are_gone()
    {
        var list = await NewListAsync();

        using var read = await GetAsync($"/api/v1/cards?listId={list.Id}");
        using var create = await PostAsync("/api/v1/cards", new
        {
            title = "Write the brief",
            isDueComplete = false,
            listId = list.Id
        });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await CardsAsync(list.Id));
    }

    private async Task<ListDto> NewListAsync() =>
        await CreateListAsync((await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap")).Id,
            "To do");

    private async Task<CardDto> PostCardAsync(Guid listId, string title, DateTime dueDate, DateTime startDate)
    {
        using var response = await PostAsync(CardsPath(listId), new
        {
            title,
            dueDate,
            startDate,
            isDueComplete = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<CardDto>()).Data!;
    }

    private async Task<List<CardDto>> CardsAsync(Guid listId, string? query = null) =>
        (await ReadCardsAsync(listId, query)).Entries;
}
