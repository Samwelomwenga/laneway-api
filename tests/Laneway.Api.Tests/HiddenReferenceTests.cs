using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class HiddenReferenceTests : AccessMatrix
{
    public HiddenReferenceTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_hidden_label_in_labelIds_reads_as_missing()
    {
        var (place, hidden, readable, member) = await SeedAsync();

        var errors = await ErrorsAsync(PostAsync(CardsPath(place.Content.ListId), new
        {
            title = "Labelled",
            isDueComplete = false,
            labelIds = new[] { hidden.LabelId, readable.LabelId }
        }, member));

        Assert.Equal(["labelIds[0] notFound", "labelIds[1] notOnBoard"], errors);
    }

    [Fact]
    public async Task A_hidden_cover_attachment_reads_as_missing()
    {
        var (place, hidden, readable, member) = await SeedAsync();
        var cover = $"/api/v1/cards/{place.Content.CardId}/cover";

        Assert.Equal(["attachmentId notFound"],
            await ErrorsAsync(PutAsync(cover, new { attachmentId = hidden.AttachmentId }, member)));
        Assert.Equal(["attachmentId notOnCard"],
            await ErrorsAsync(PutAsync(cover, new { attachmentId = readable.AttachmentId }, member)));
    }

    [Fact]
    public async Task A_hidden_checklist_for_a_check_item_move_reads_as_missing()
    {
        var (place, hidden, readable, member) = await SeedAsync();
        var move = $"/api/v1/check-items/{place.Content.CheckItemId}/position";

        Assert.Equal(["checklistId notFound"],
            await ErrorsAsync(PutAsync(move, new { checklistId = hidden.ChecklistId }, member)));
        Assert.Equal(["checklistId notOnCard"],
            await ErrorsAsync(PutAsync(move, new { checklistId = readable.ChecklistId }, member)));
    }

    [Fact]
    public async Task A_hidden_anchor_reads_as_missing()
    {
        var (place, hidden, readable, member) = await SeedAsync();

        Assert.Equal(["after notFound"],
            await ErrorsAsync(PostAsync(ListsPath(place.BoardId), new { name = "Anchored", after = hidden.ListId }, member)));
        Assert.Equal(["before notSibling"],
            await ErrorsAsync(PostAsync(ListsPath(place.BoardId), new { name = "Anchored", before = readable.ListId }, member)));
        Assert.Equal(["after notFound"],
            await ErrorsAsync(PostAsync(CardsPath(place.Content.ListId),
                new { title = "Anchored", isDueComplete = false, after = hidden.CardId }, member)));
        Assert.Equal(["before notFound"],
            await ErrorsAsync(PostAsync(ChecklistsPath(place.Content.CardId),
                new { name = "Anchored", before = hidden.ChecklistId }, member)));
        Assert.Equal(["after notFound"],
            await ErrorsAsync(PostAsync(CheckItemsPath(place.Content.ChecklistId),
                new { name = "Anchored", after = hidden.CheckItemId }, member)));
    }

    [Fact]
    public async Task A_hidden_label_on_the_card_label_route_is_not_found()
    {
        var (place, hidden, readable, member) = await SeedAsync();

        using var response = await PostAsync(
            $"/api/v1/cards/{place.Content.CardId}/labels/{hidden.LabelId}", new { }, member);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Label not found",
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());

        Assert.Equal(["labelId notOnBoard"], await ErrorsAsync(PostAsync(
            $"/api/v1/cards/{place.Content.CardId}/labels/{readable.LabelId}", new { }, member)));
    }

    [Fact]
    public async Task Body_ids_are_checked_before_the_archived_card()
    {
        var (place, hidden, _, member) = await SeedAsync();
        await FromDatabaseAsync(async database =>
        {
            var card = await database.Cards.FindAsync(place.Content.CardId);
            card!.IsArchived = true;
            return await database.SaveChangesAsync();
        });

        Assert.Equal(["labelIds[0] notFound"], await ErrorsAsync(PutAsync($"/api/v1/cards/{place.Content.CardId}",
            new { title = "Renamed", isDueComplete = false, labelIds = new[] { hidden.LabelId } }, member)));
        Assert.Equal(["attachmentId notFound"], await ErrorsAsync(PutAsync(
            $"/api/v1/cards/{place.Content.CardId}/cover", new { attachmentId = hidden.AttachmentId }, member)));
        Assert.Equal(["checklistId notFound"], await ErrorsAsync(PutAsync(
            $"/api/v1/check-items/{place.Content.CheckItemId}/position", new { checklistId = hidden.ChecklistId }, member)));
    }

    private async Task<(Place Place, Content Hidden, Content Readable, TestUser Member)> SeedAsync()
    {
        var callers = await CallersAsync();
        var place = await SeedPlaceAsync(WorkspaceVisibility.Private, BoardVisibility.Private);
        var hidden = await SeedContentAsync(await SeedBoardAsync(place.WorkspaceId, BoardVisibility.Private));
        var readable = await SeedContentAsync(await SeedBoardAsync(place.WorkspaceId, BoardVisibility.Public));
        return (place, hidden, readable, callers[Caller.BoardNormal]);
    }

    private static async Task<string[]> ErrorsAsync(Task<HttpResponseMessage> send)
    {
        using var response = await send;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => $"{error.GetProperty("field").GetString()} {error.GetProperty("code").GetString()}")
            .ToArray();
    }
}
