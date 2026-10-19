using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AttachmentAccessTests : AccessMatrix
{
    public AttachmentAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public Task GET_a_cards_attachments() =>
        RunBoardListRowAsync("Card", place => $"/api/v1/cards/{place.Content.CardId}/attachments",
            content => [content.AttachmentId, content.FileAttachmentId]);

    [Fact]
    public Task GET_an_attachment() =>
        RunBoardRowAsync(Act.ReadBoard, 200, (place, caller) =>
            GetAsync($"/api/v1/attachments/{place.Content.AttachmentId}", caller), routeObject: "Attachment");

    [Fact]
    public async Task GET_an_attachments_content()
    {
        await StoreMatrixFileAsync();
        await RunBoardRowAsync(Act.ReadBoard, 302, (place, caller) =>
            GetWithoutRedirectAsync($"/api/v1/attachments/{place.Content.FileAttachmentId}/content", caller),
            routeObject: "Attachment");
    }

    [Fact]
    public Task POST_a_file_attachment() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
        {
            var form = new MultipartFormDataContent
            {
                { new ByteArrayContent("A line of notes.\n"u8.ToArray()), "file", "notes.txt" }
            };
            return PostAsync($"/api/v1/cards/{place.Content.CardId}/attachments", form, caller);
        }, routeObject: "Card");

    [Fact]
    public Task POST_a_link_attachment() =>
        RunBoardRowAsync(Act.EditBoard, 201, (place, caller) =>
            PostAsync($"/api/v1/cards/{place.Content.CardId}/attachments",
                new { url = "https://example.com/new" }, caller), routeObject: "Card");

    [Fact]
    public Task PUT_an_attachment() =>
        RunBoardRowAsync(Act.EditBoard, 200, (place, caller) =>
            PutAsync($"/api/v1/attachments/{place.Content.AttachmentId}", new { name = "Renamed" }, caller),
            routeObject: "Attachment");

    [Fact]
    public Task DELETE_an_attachment() =>
        RunBoardRowAsync(Act.EditBoard, 204, (place, caller) =>
            DeleteAsync($"/api/v1/attachments/{place.Content.AttachmentId}", caller), routeObject: "Attachment");
}
