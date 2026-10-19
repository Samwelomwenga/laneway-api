using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CommentAccessTests : AccessMatrix
{
    public CommentAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task POST_a_comment() =>
        RunBoardRowAsync(Act.CommentOnBoard, 201, (place, caller) =>
            PostAsync(CommentPath(place.Content.CardId), new { text = "Looks good." }, caller), routeObject: "Card");

    [Fact]
    public Task PUT_a_comment() =>
        RunBoardRowAsync(Act.CommentOnBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/comments/{place.Content.CommentIds[caller.Id]}", new { text = "Edited." }, caller),
            routeObject: "Comment");

    [Fact]
    public Task DELETE_a_comment() =>
        RunBoardRowAsync(Act.CommentOnBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/comments/{place.Content.CommentIds[caller.Id]}", caller), routeObject: "Comment");
}
