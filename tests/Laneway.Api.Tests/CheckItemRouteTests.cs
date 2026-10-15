using System.Net;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CheckItemRouteTests : ApiTests
{
    public CheckItemRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_checklist_lists_only_its_own_check_items()
    {
        var checklist = await NewChecklistAsync();
        var other = await CreateChecklistAsync(checklist.CardId, "Later");
        var outline = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        var review = await CreateCheckItemAsync(checklist.Id, "Send for review", isChecked: true);
        await CreateCheckItemAsync(other.Id, "Book the studio");

        var checkItems = await ReadCheckItemsAsync(checklist.Id);

        Assert.Equal([outline.Id, review.Id], checkItems.Select(c => c.Id));
        Assert.All(checkItems, c => Assert.Equal(checklist.Id, c.ChecklistId));
    }

    [Fact]
    public async Task The_collection_takes_archived_exclude_and_include()
    {
        var checklist = await NewChecklistAsync();
        var outline = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        Assert.Equal(outline.Id, Assert.Single(await ReadCheckItemsAsync(checklist.Id, "archived=exclude")).Id);
        Assert.Equal(outline.Id, Assert.Single(await ReadCheckItemsAsync(checklist.Id, "archived=include")).Id);
    }

    [Fact]
    public async Task Archived_only_is_unknownValue()
    {
        var checklist = await NewChecklistAsync();

        using var response = await GetAsync($"{CheckItemsPath(checklist.Id)}?archived=only");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("archived", error.Field);
        Assert.Equal(ErrorCodes.UnknownValue, error.Code);
    }

    [Fact]
    public async Task A_create_puts_the_check_item_on_the_path_checklist()
    {
        var checklist = await NewChecklistAsync();
        var last = await CreateCheckItemAsync(checklist.Id, "Send for review");

        using var response = await PostAsync(
            CheckItemsPath(checklist.Id), new { name = "Draft the outline", isChecked = true, before = last.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkItem = (await response.ReadEnvelope<CheckItemDto>()).Data!;
        Assert.Equal(checklist.Id, checkItem.ChecklistId);
        Assert.True(checkItem.IsChecked);
        Assert.Equal($"/api/v1/check-items/{checkItem.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal([checkItem.Id, last.Id], (await ReadCheckItemsAsync(checklist.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task A_create_takes_a_position()
    {
        var checklist = await NewChecklistAsync();
        var first = await CreateCheckItemAsync(checklist.Id, "Draft the outline");

        using var response = await PostAsync(CheckItemsPath(checklist.Id), new { name = "Warm up", position = "top" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkItem = (await response.ReadEnvelope<CheckItemDto>()).Data!;
        Assert.Equal([checkItem.Id, first.Id], (await ReadCheckItemsAsync(checklist.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task An_anchor_on_another_checklist_is_notSibling()
    {
        var checklist = await NewChecklistAsync();
        var other = await CreateChecklistAsync(checklist.CardId, "Later");
        var elsewhere = await CreateCheckItemAsync(other.Id, "Book the studio");

        using var response = await PostAsync(
            CheckItemsPath(checklist.Id), new { name = "Draft the outline", after = elsewhere.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("after", error.Field);
        Assert.Equal(ErrorCodes.NotSibling, error.Code);
    }

    [Fact]
    public async Task An_unknown_checklist_is_404_on_the_read_and_the_create()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync(CheckItemsPath(missing));
        using var create = await PostAsync(CheckItemsPath(missing), new { name = "Draft the outline" });

        foreach (var response in new[] { read, create })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Checklist not found", (await response.ReadEnvelope<object>()).Message);
        }
    }

    [Fact]
    public async Task ChecklistId_in_the_query_is_unknownField()
    {
        var checklist = await NewChecklistAsync();

        using var response = await GetAsync($"{CheckItemsPath(checklist.Id)}?checklistId={checklist.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("checklistId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
    }

    [Fact]
    public async Task ChecklistId_in_the_create_body_is_unknownField()
    {
        var checklist = await NewChecklistAsync();

        using var response = await PostAsync(
            CheckItemsPath(checklist.Id), new { name = "Draft the outline", checklistId = checklist.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("checklistId", error.Field);
        Assert.Equal(ErrorCodes.UnknownField, error.Code);
        Assert.Empty(await ReadCheckItemsAsync(checklist.Id));
    }

    [Fact]
    public async Task Body_and_query_errors_come_back_before_the_checklist_is_looked_up()
    {
        var missing = Guid.NewGuid();

        using var read = await GetAsync($"{CheckItemsPath(missing)}?archived=only&pageSize=0");
        using var create = await PostAsync(CheckItemsPath(missing), new { position = "middle" });

        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.Equal(["archived", "pageSize"], (await read.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(["name", "position"], (await create.ReadErrors()).Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_archived_checklist_still_lists_its_check_items_and_refuses_a_create()
    {
        var checklist = await NewChecklistAsync();
        var outline = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        await SetChecklistArchivedAsync(checklist.Id, true);

        using var response = await PostAsync(CheckItemsPath(checklist.Id), new { name = "Send for review" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Equal(outline.Id, Assert.Single(await ReadCheckItemsAsync(checklist.Id)).Id);
    }

    [Fact]
    public async Task A_checklist_on_an_archived_card_still_lists_its_check_items_and_refuses_a_create()
    {
        var checklist = await NewChecklistAsync();
        var outline = await CreateCheckItemAsync(checklist.Id, "Draft the outline");
        await SetCardArchivedAsync(checklist.CardId, true);

        using var response = await PostAsync(CheckItemsPath(checklist.Id), new { name = "Send for review" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.Archived, error.Code);
        Assert.Equal(outline.Id, Assert.Single(await ReadCheckItemsAsync(checklist.Id)).Id);
    }

    [Fact]
    public async Task A_create_into_a_full_checklist_is_limitReached_with_no_field()
    {
        var checklist = await NewChecklistAsync();
        await FillAsync(checklist.Id, FieldLimits.CheckItemsPerChecklist);

        using var response = await PostAsync(CheckItemsPath(checklist.Id), new { name = "One too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Null(error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
    }

    [Fact]
    public async Task A_move_into_a_full_checklist_is_limitReached_on_checklistId()
    {
        var checklist = await NewChecklistAsync();
        var other = await CreateChecklistAsync(checklist.CardId, "Later");
        var mover = await CreateCheckItemAsync(other.Id, "Book the studio");
        await FillAsync(checklist.Id, FieldLimits.CheckItemsPerChecklist);

        using var response = await MoveCheckItemAsync(mover.Id, checklist.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("checklistId", error.Field);
        Assert.Equal(ErrorCodes.LimitReached, error.Code);
    }

    [Fact]
    public async Task The_flat_check_item_collection_routes_are_gone()
    {
        var checklist = await NewChecklistAsync();

        using var read = await GetAsync($"/api/v1/check-items?checklistId={checklist.Id}");
        using var create = await PostAsync(
            "/api/v1/check-items", new { name = "Draft the outline", checklistId = checklist.Id });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Empty(await ReadCheckItemsAsync(checklist.Id));
    }

    private Task<int> FillAsync(Guid checklistId, int count) =>
        FromDatabaseAsync(context =>
        {
            context.CheckItems.AddRange(Enumerable.Range(1, count).Select(i => new CheckItem
            {
                Id = Guid.NewGuid(),
                ChecklistId = checklistId,
                Name = $"Step {i}",
                Position = i * 65536d,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ActorId
            }));
            return context.SaveChangesAsync();
        });

    private async Task<ChecklistDto> NewChecklistAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        var card = await CreateCardAsync((await CreateListAsync(board.Id, "To do")).Id, "Write the brief");
        return await CreateChecklistAsync(card.Id, "Steps");
    }
}
