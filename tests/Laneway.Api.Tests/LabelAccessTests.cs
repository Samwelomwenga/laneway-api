using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class LabelAccessTests : AccessMatrix
{
    public LabelAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_boards_labels() =>
        RunBoardListRowAsync("Board", place => LabelsPath(place.BoardId),
            content => [content.LabelId, content.CardLabelId]);

    [Fact]
    public Task GET_a_label() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/labels/{place.Content.LabelId}", caller), routeObject: "Label");

    [Fact]
    public Task POST_a_label() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync(LabelsPath(place.BoardId), new { name = "New label", color = "Blue" }, caller));

    [Fact]
    public Task PUT_a_label() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/labels/{place.Content.LabelId}", new { name = "Renamed" }, caller),
            routeObject: "Label");

    [Fact]
    public Task DELETE_a_label() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/labels/{place.Content.LabelId}", caller), routeObject: "Label");
}
