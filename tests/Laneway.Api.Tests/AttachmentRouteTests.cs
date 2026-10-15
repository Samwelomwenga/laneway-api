using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class AttachmentRouteTests : ApiTests
{
    public AttachmentRouteTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task An_attachment_reads_at_its_own_url()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        using var response = await GetAsync($"/api/v1/attachments/{attachment.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = (await response.ReadEnvelope<AttachmentDto>()).Data!;
        Assert.Equal(attachment.Id, read.Id);
        Assert.Equal(card.Id, read.CardId);
        Assert.Equal("The mock", read.Name);
    }

    [Fact]
    public async Task A_file_downloads_from_its_content_url()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "notes.txt");

        var bytes = await DownloadAttachmentAsync(attachment.Id);

        Assert.Equal("A line of notes.\n"u8.ToArray(), bytes);
    }

    [Fact]
    public async Task An_attachment_renames_at_its_own_url()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");

        using var response = await RenameAttachmentAsync(attachment.Id, "The final mock");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("The final mock", (await response.ReadEnvelope<AttachmentDto>()).Data!.Name);
        Assert.Equal("The final mock", Assert.Single(await ReadAttachmentsAsync(card.Id)).Name);
    }

    [Fact]
    public async Task An_attachment_deletes_at_its_own_url()
    {
        var card = await NewCardAsync();
        var attachment = await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief");

        await DeleteAttachmentAsync(attachment.Id);

        using var read = await GetAsync($"/api/v1/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Empty(await ReadAttachmentsAsync(card.Id));
    }

    [Fact]
    public async Task An_unknown_attachment_is_404_on_every_route()
    {
        var unknown = $"/api/v1/attachments/{Guid.NewGuid()}";

        using var read = await GetAsync(unknown);
        using var content = await GetAsync($"{unknown}/content");
        using var rename = await PutAsync(unknown, new { name = "The mock" });
        using var delete = await DeleteAsync(unknown);

        Assert.All(
            [read, content, rename, delete],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal("Attachment not found", (await read.ReadEnvelope<AttachmentDto>()).Message);
    }

    [Fact]
    public async Task A_link_has_no_content_to_download()
    {
        var card = await NewCardAsync();
        var attachment = await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief");

        using var response = await GetAsync($"/api/v1/attachments/{attachment.Id}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_card_lists_and_creates_files_and_links_as_before()
    {
        var card = await NewCardAsync();
        var file = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        var link = await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief", "The brief");

        var attachments = await ReadAttachmentsAsync(card.Id);

        Assert.Equal([link.Id, file.Id], attachments.Select(a => a.Id));
        Assert.All(attachments, a => Assert.Equal(card.Id, a.CardId));
    }

    [Fact]
    public async Task A_create_points_its_location_at_the_attachment_url()
    {
        var card = await NewCardAsync();

        using var response = await PostAsync(
            $"/api/v1/cards/{card.Id}/attachments", new { url = "https://example.com/brief" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = (await response.ReadEnvelope<AttachmentDto>()).Data!;
        Assert.Equal($"/api/v1/attachments/{link.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
    }

    [Fact]
    public async Task A_file_url_is_its_content_url_and_a_link_url_is_the_link()
    {
        var card = await NewCardAsync();
        var file = await UploadAttachmentAsync(card.Id, "shot.png");
        var link = await CreateLinkAttachmentAsync(card.Id, "https://example.com/brief");

        Assert.Equal($"/api/v1/attachments/{file.Id}/content", file.Url);
        Assert.Equal("https://example.com/brief", link.Url);
        Assert.Equal(
            $"/api/v1/attachments/{file.Id}/content",
            (await ReadAttachmentsAsync(card.Id)).Single(a => a.Id == file.Id).Url);
    }

    [Fact]
    public async Task An_image_cover_url_is_the_attachment_content_url()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png");
        await SetCoverAsync(card.Id, attachmentId: attachment.Id);

        var read = await ReadCardAsync(card.Id);

        Assert.Equal(attachment.Id, read.Cover!.AttachmentId);
        Assert.Equal($"/api/v1/attachments/{attachment.Id}/content", read.Cover.Url);
    }

    [Fact]
    public async Task The_storage_key_still_names_the_card()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png");

        var objectKey = await FromDatabaseAsync(context => context.Attachments
            .Where(a => a.Id == attachment.Id)
            .Select(a => a.ObjectKey)
            .SingleAsync());

        Assert.Equal($"cards/{card.Id}/{attachment.Id}", objectKey);
    }

    [Fact]
    public async Task The_by_id_routes_under_the_card_are_gone()
    {
        var card = await NewCardAsync();
        var attachment = await UploadAttachmentAsync(card.Id, "shot.png", "The mock");
        var old = $"/api/v1/cards/{card.Id}/attachments/{attachment.Id}";

        using var read = await GetAsync(old);
        using var content = await GetAsync($"{old}/content");
        using var rename = await PutAsync(old, new { name = "The final mock" });
        using var delete = await DeleteAsync(old);

        Assert.All(
            [read, content, rename, delete],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal("The mock", Assert.Single(await ReadAttachmentsAsync(card.Id)).Name);
    }

    private async Task<CardDto> NewCardAsync()
    {
        var board = await CreateBoardAsync((await CreateWorkspaceAsync("Design")).Id, "Roadmap");
        return await CreateCardAsync((await CreateListAsync(board.Id, "To do")).Id, "Write the brief");
    }
}
