using System.Net;
using System.Text.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CardViewTests : ApiTests
{
    public CardViewTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task The_card_view_holds_its_open_checklists_in_position_order_with_their_check_items()
    {
        var card = await NewCardAsync();
        var ship = await CreateChecklistAsync(card.Id, "Ship");
        var plan = await CreateChecklistAsync(card.Id, "Plan", position: "top");
        var old = await CreateChecklistAsync(card.Id, "Old");
        await SetChecklistArchivedAsync(old.Id, true);
        var draft = await CreateCheckItemAsync(plan.Id, "Draft");
        var outline = await CreateCheckItemAsync(plan.Id, "Outline", position: "top");

        var view = await ReadCardViewAsync(card.Id);

        Assert.Equal(card.Id, view.Id);
        Assert.Equal("Write the brief", view.Title);
        Assert.Equal([plan.Id, ship.Id], view.Checklists.Select(c => c.Id));
        Assert.Equal([outline.Id, draft.Id], view.Checklists[0].CheckItems.Select(i => i.Id));
        Assert.Empty(view.Checklists[1].CheckItems);
    }

    [Fact]
    public async Task The_card_view_holds_its_attachments_newest_first()
    {
        var card = await NewCardAsync();
        var spec = await CreateLinkAttachmentAsync(card.Id, "https://example.com/spec", "Spec");
        var shot = await UploadAttachmentAsync(card.Id, "shot.png");
        var notes = await CreateLinkAttachmentAsync(card.Id, "https://example.com/notes", "Notes");

        var view = await ReadCardViewAsync(card.Id);

        Assert.Equal([notes.Id, shot.Id, spec.Id], view.Attachments.Select(a => a.Id));
        Assert.Equal($"/api/v1/attachments/{shot.Id}/content", view.Attachments[1].Url);
    }

    [Fact]
    public async Task The_card_view_counts_comments_but_holds_none()
    {
        var card = await NewCardAsync();
        await CreateCommentAsync(card.Id, "Looks good");

        using var response = await GetAsync($"/api/v1/cards/{card.Id}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(1, data.GetProperty("commentCount").GetInt32());
        Assert.False(data.Has("comments"), data.GetRawText());
    }

    [Fact]
    public async Task Card_writes_return_the_plain_card()
    {
        var card = await NewCardAsync();
        await CreateChecklistAsync(card.Id, "Plan");
        await CreateLinkAttachmentAsync(card.Id, "https://example.com/spec", "Spec");

        await AssertPlainAsync(() => PostAsync(CardsPath(card.ListId), new { title = "Book the studio", isDueComplete = false }));
        await AssertPlainAsync(() => UpdateCardAsync(card.Id, "Write the long brief"));
        await AssertPlainAsync(() => MoveCardAsync(card.Id, card.ListId, position: "top"));
        await AssertPlainAsync(() => PostAsync(CopyPath(card.Id), new { listId = card.ListId }));
    }

    private static async Task AssertPlainAsync(Func<Task<HttpResponseMessage>> send)
    {
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.False(data.Has("checklists"), data.GetRawText());
        Assert.False(data.Has("attachments"), data.GetRawText());
    }

    private async Task<CardDto> NewCardAsync()
    {
        var workspace = await CreateWorkspaceAsync("Design");
        var board = await CreateBoardAsync(workspace.Id, "Roadmap");
        var list = await CreateListAsync(board.Id, "To do");
        return await CreateCardAsync(list.Id, "Write the brief");
    }

    private async Task<CardView> ReadCardViewAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/cards/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<CardView>()).Data!;
    }
}

public sealed record CardView(
    Guid Id,
    string Title,
    List<ChecklistDto> Checklists,
    List<AttachmentDto> Attachments);
