using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BoardAccessTests : AccessMatrix
{
    public BoardAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_board() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/boards/{place.BoardId}", caller));

    [Fact]
    public Task PUT_a_board() =>
        RunBoardRowAsync(Act.AdministerBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/boards/{place.BoardId}", new { name = "Renamed", visibility = "Public" }, caller));

    [Fact]
    public Task PUT_a_boards_archived_state() =>
        RunBoardRowAsync(Act.AdministerBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/boards/{place.BoardId}/archived", new { value = true }, caller));

    [Fact]
    public Task DELETE_a_board() =>
        RunBoardRowAsync(Act.AdministerBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/boards/{place.BoardId}", caller), archived: true);
}
