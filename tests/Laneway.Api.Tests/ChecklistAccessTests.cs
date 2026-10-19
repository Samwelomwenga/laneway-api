using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ChecklistAccessTests : AccessMatrix
{
    public ChecklistAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_cards_checklists() =>
        RunBoardListRowAsync("Card", place => ChecklistsPath(place.Content.CardId), content => [content.ChecklistId]);

    [Fact]
    public Task GET_a_checklist() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/checklists/{place.Content.ChecklistId}", caller), routeObject: "Checklist");

    [Fact]
    public Task POST_a_checklist() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync(ChecklistsPath(place.Content.CardId), new { name = "New checklist" }, caller),
            routeObject: "Card");

    [Fact]
    public Task PUT_a_checklist() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/checklists/{place.Content.ChecklistId}", new { name = "Renamed" }, caller),
            routeObject: "Checklist");

    [Fact]
    public Task PUT_a_checklists_position() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/checklists/{place.Content.ChecklistId}/position", new { position = "bottom" }, caller),
            routeObject: "Checklist");

    [Fact]
    public Task PUT_a_checklists_archived_state() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/checklists/{place.Content.ChecklistId}/archived", new { value = true }, caller),
            routeObject: "Checklist");

    [Fact]
    public Task DELETE_a_checklist() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/checklists/{place.Content.ArchivedChecklistId}", caller), routeObject: "Checklist");
}
