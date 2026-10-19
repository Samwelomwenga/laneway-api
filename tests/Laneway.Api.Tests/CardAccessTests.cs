using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CardAccessTests : AccessMatrix
{
    public CardAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_lists_cards() =>
        RunBoardListRowAsync("List", place => CardsPath(place.Content.ListId), content => [content.CardId]);

    [Fact]
    public Task GET_a_card() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/cards/{place.Content.CardId}", caller), routeObject: "Card");

    [Fact]
    public Task POST_a_card() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync(CardsPath(place.Content.ListId), new { title = "New card", isDueComplete = false }, caller),
            routeObject: "List");

    [Fact]
    public Task PUT_a_card() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/cards/{place.Content.CardId}", new { title = "Renamed", isDueComplete = false }, caller),
            routeObject: "Card");

    [Fact]
    public Task PUT_a_cards_cover() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/cards/{place.Content.CardId}/cover", new { color = "Blue" }, caller),
            routeObject: "Card");

    [Fact]
    public Task PUT_a_cards_archived_state() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PutAsync($"/api/v1/cards/{place.Content.CardId}/archived", new { value = true }, caller),
            routeObject: "Card");

    [Fact]
    public Task DELETE_a_card() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/cards/{place.Content.ArchivedCardId}", caller), routeObject: "Card");

    [Fact]
    public Task POST_a_label_onto_a_card() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            PostAsync($"/api/v1/cards/{place.Content.CardId}/labels/{place.Content.LabelId}", new { }, caller),
            routeObject: "Card");

    [Fact]
    public Task DELETE_a_label_from_a_card() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/cards/{place.Content.CardId}/labels/{place.Content.CardLabelId}", caller),
            routeObject: "Card");

    [Fact]
    public Task PUT_a_cards_position() =>
        RunMoveRowAsync(Act.EditBoard, "listId", 200, (from, to, caller) =>
            PutAsync($"/api/v1/cards/{from.Content.CardId}/position", new { listId = to.Content.ListId }, caller),
            routeObject: "Card");

    [Fact]
    public Task POST_a_card_copy() =>
        RunMoveRowAsync(Act.ReadBoard, "listId", 201, (from, to, caller) =>
            PostAsync($"/api/v1/cards/{from.Content.CardId}/copies", new { listId = to.Content.ListId }, caller),
            routeObject: "Card");
}
