using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ChecklistRouteTests : ApiTests
{
    public ChecklistRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_card_lists_only_its_own_checklists_with_their_check_items()
    {
        var card = await NewCardAsync();
        var other = await CreateCardAsync(card.ListId, "Book the studio");
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        var item = await CreateCheckItemAsync(steps.Id, "Draft the outline");
        await CreateChecklistAsync(other.Id, "Steps");

        var checklist = Assert.Single(await ReadChecklistsAsync(card.Id));

        Assert.Equal(steps.Id, checklist.Id);
        Assert.Equal(card.Id, checklist.CardId);
        Assert.Equal(item.Id, Assert.Single(checklist.CheckItems).Id);
    }

    [Fact]
    public async Task The_collection_filters_by_archived()
    {
        var card = await NewCardAsync();
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        var later = await CreateChecklistAsync(card.Id, "Later");
        await SetChecklistArchivedAsync(later.Id, true);

        Assert.Equal(steps.Id, Assert.Single(await ReadChecklistsAsync(card.Id)).Id);
        Assert.Equal(later.Id, Assert.Single(await ReadChecklistsAsync(card.Id, "archived=only")).Id);
        Assert.Equal([steps.Id, later.Id], (await ReadChecklistsAsync(card.Id, "archived=include")).Select(c => c.Id));
    }

    [Fact]
    public async Task A_create_puts_the_checklist_on_the_path_card()
    {
        var card = await NewCardAsync();
        var last = await CreateChecklistAsync(card.Id, "Later");

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Steps", before = last.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checklist = (await response.ReadEnvelope<ChecklistDto>()).Data!;
        Assert.Equal(card.Id, checklist.CardId);
        Assert.Empty(checklist.CheckItems);
        Assert.Equal($"/api/v1/checklists/{checklist.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal([checklist.Id, last.Id], (await ReadChecklistsAsync(card.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task A_create_takes_a_position()
    {
        var card = await NewCardAsync();
        var first = await CreateChecklistAsync(card.Id, "Steps");

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Warm-up", position = "top" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checklist = (await response.ReadEnvelope<ChecklistDto>()).Data!;
        Assert.Equal([checklist.Id, first.Id], (await ReadChecklistsAsync(card.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task An_anchor_on_another_card_is_notSibling()
    {
        var card = await NewCardAsync();
        var other = await CreateCardAsync(card.ListId, "Book the studio");
        var elsewhere = await CreateChecklistAsync(other.Id, "Steps");

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Steps", after = elsewhere.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task An_unknown_card_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(ChecklistsPath(missing));
        using var create = await PostAsync(ChecklistsPath(missing), new { name = "Steps" });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Card not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task CardId_in_the_query_is_unknownField()
    {
        var card = await NewCardAsync();

        using var response = await GetAsync($"{ChecklistsPath(card.Id)}?cardId={card.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("cardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task CardId_in_the_create_body_is_unknownField()
    {
        var card = await NewCardAsync();

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Steps", cardId = card.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("cardId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
        Assert.Empty(await ReadChecklistsAsync(card.Id, "archived=include"));
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_card_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{ChecklistsPath(missing)}?archived=Nope&pageSize=0");
        using var create = await PostAsync(ChecklistsPath(missing), new { position = "middle" });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["archived", "pageSize"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(["name", "position"], (await create.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_archived_card_still_lists_its_checklists()
    {
        var card = await NewCardAsync();
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        var later = await CreateChecklistAsync(card.Id, "Later");
        await SetChecklistArchivedAsync(later.Id, true);
        await SetCardArchivedAsync(card.Id, true);

        Assert.Equal(steps.Id, Assert.Single(await ReadChecklistsAsync(card.Id)).Id);
        Assert.Equal(later.Id, Assert.Single(await ReadChecklistsAsync(card.Id, "archived=only")).Id);
    }

    [Fact]
    public async Task A_card_on_an_archived_board_still_lists_its_checklists_and_refuses_a_create()
    {
        var card = await NewCardAsync();
        var steps = await CreateChecklistAsync(card.Id, "Steps");
        await SetBoardArchivedAsync((await ReadListAsync(card.ListId)).BoardId, true);

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Later" });

        Assert.Equal(steps.Id, Assert.Single(await ReadChecklistsAsync(card.Id)).Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
    }

    [Fact]
    public async Task A_create_on_an_archived_card_is_409_archived_with_no_field()
    {
        var card = await NewCardAsync();
        await SetCardArchivedAsync(card.Id, true);

        using var response = await PostAsync(ChecklistsPath(card.Id), new { name = "Steps" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Empty(await ReadChecklistsAsync(card.Id, "archived=include"));
    }

    [Fact]
    public async Task The_flat_checklist_collection_routes_are_gone()
    {
        var card = await NewCardAsync();

        using var read = await GetAsync($"/api/v1/checklists?cardId={card.Id}");
        using var create = await PostAsync("/api/v1/checklists", new { name = "Steps", cardId = card.Id });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await ReadChecklistsAsync(card.Id, "archived=include"));
    }

    private async Task<CardDto> NewCardAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        return await CreateCardAsync((await CreateListAsync(board.Id, "To do")).Id, "Write the brief");
    }
}
