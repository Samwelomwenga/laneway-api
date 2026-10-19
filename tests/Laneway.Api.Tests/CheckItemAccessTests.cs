using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CheckItemAccessTests : AccessMatrix
{
    public CheckItemAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_checklists_check_items() =>
        RunBoardListRowAsync("Checklist", place => CheckItemsPath(place.Content.ChecklistId),
            content => [content.CheckItemId]);

    [Fact]
    public Task GET_a_check_item() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/check-items/{place.Content.CheckItemId}", caller), routeObject: "Check item");

    [Fact]
    public Task POST_a_check_item() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync(CheckItemsPath(place.Content.ChecklistId), new { name = "New item" }, caller),
            routeObject: "Checklist");

    [Fact]
    public Task PUT_a_check_item() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/check-items/{place.Content.CheckItemId}", new { name = "Renamed" }, caller),
            routeObject: "Check item");

    [Fact]
    public Task PUT_a_check_items_position() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/check-items/{place.Content.CheckItemId}/position",
                new { checklistId = place.Content.ChecklistId, position = "bottom" }, caller),
            routeObject: "Check item");

    [Fact]
    public Task PUT_a_check_items_checked_state() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/check-items/{place.Content.CheckItemId}/checked", new { value = true }, caller),
            routeObject: "Check item");

    [Fact]
    public Task DELETE_a_check_item() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/check-items/{place.Content.CheckItemId}", caller), routeObject: "Check item");
}
