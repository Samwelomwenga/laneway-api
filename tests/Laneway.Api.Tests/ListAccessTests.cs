using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ListAccessTests : AccessMatrix
{
    public ListAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_boards_lists() =>
        RunBoardListRowAsync("Board", place => ListsPath(place.BoardId), content => [content.ListId]);

    [Fact]
    public Task GET_a_list() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/lists/{place.Content.ListId}", caller), routeObject: "List");

    [Fact]
    public Task POST_a_list() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync(ListsPath(place.BoardId), new { name = "New list" }, caller));

    [Fact]
    public Task PUT_a_list() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/lists/{place.Content.ListId}", new { name = "Renamed" }, caller), routeObject: "List");

    [Fact]
    public Task PUT_a_lists_archived_state() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/lists/{place.Content.ListId}/archived", new { value = true }, caller),
            routeObject: "List");

    [Fact]
    public Task DELETE_a_list() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/lists/{place.Content.ArchivedListId}", caller), routeObject: "List");

    [Fact]
    public Task PUT_a_lists_position() =>
        RunMoveRowAsync(Act.EditBoard, "boardId", 200, (from, to, caller) =>
            PutAsync($"/api/v1/lists/{from.Content.ListId}/position", new { boardId = to.BoardId }, caller),
            routeObject: "List");

    [Fact]
    public Task POST_a_list_copy() =>
        RunMoveRowAsync(Act.ReadBoard, "boardId", 202, (from, to, caller) =>
            PostAsync($"/api/v1/lists/{from.Content.ListId}/copies", new { boardId = to.BoardId }, caller),
            routeObject: "List");
}
