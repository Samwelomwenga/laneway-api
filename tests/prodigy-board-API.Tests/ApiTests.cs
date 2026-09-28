using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DefaultNamespace;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace prodigy_board_API.Tests;

public abstract class ApiTests : IAsyncLifetime
{
    private readonly TestStack _stack;

    private ApiFactory? _factory;
    private HttpClient? _client;
    private HttpClient? _noRedirects;
    private Guid? _databaseId;

    protected ApiTests(TestStack stack) => _stack = stack;

    protected UserDto ActorUser { get; private set; } = null!;

    protected Guid ActorId => ActorUser.Id;

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host isn't up yet.");

    private HttpClient NoRedirects =>
        _noRedirects ??= (_factory ?? throw new InvalidOperationException("The test host isn't up yet."))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task InitializeAsync()
    {
        _databaseId = await _stack.NewDatabaseAsync();
        _factory = new ApiFactory(_stack.ConnectionStringFor(_databaseId.Value));
        _client = _factory.CreateApiClient();
        ActorUser = await CreateUserAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _noRedirects?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_databaseId is { } databaseId)
        {
            await _stack.DropDatabaseAsync(databaseId);
        }
    }

    protected Task<HttpResponseMessage> PostAsync(string path, object body, Guid? actor = null) =>
        SendAsync(HttpMethod.Post, path, body, (actor ?? ActorId).ToString());

    protected Task<HttpResponseMessage> PutAsync(string path, object body, Guid? actor = null) =>
        SendAsync(HttpMethod.Put, path, body, (actor ?? ActorId).ToString());

    protected Task<HttpResponseMessage> DeleteAsync(string path, Guid? actor = null) =>
        SendAsync(HttpMethod.Delete, path, null, (actor ?? ActorId).ToString());

    protected Task<HttpResponseMessage> SendWithoutActorAsync(HttpMethod method, string path, object? body) =>
        SendAsync(method, path, body, actorHeader: null);

    protected Task<HttpResponseMessage> GetAsync(string path, string? actorHeader = null) =>
        SendAsync(HttpMethod.Get, path, null, actorHeader);

    protected async Task<UserDto> CreateUserAsync()
    {
        var tag = Guid.NewGuid().ToString("n")[..12];
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/users", new
        {
            username = $"actor-{tag}",
            email = $"{tag}@example.com",
            firstName = "Ana",
            lastName = "Waweru",
            language = "en-US",
            timeZone = "UTC",
            location = "Nairobi"
        }, actorHeader: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<UserDto>()).Data!;
    }

    protected async Task<WorkSpaceDto> CreateWorkspaceAsync(
        string name,
        string? description = null,
        WorkspaceVisibility visibility = WorkspaceVisibility.Public)
    {
        using var response = await PostAsync("/api/v1/workspaces", new
        {
            name,
            description,
            visibility = visibility.ToString()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<WorkSpaceDto>()).Data!;
    }

    protected async Task<HttpResponseMessage> UpdateWorkspaceAsync(
        Guid id,
        string name,
        string? description = null,
        WorkspaceVisibility visibility = WorkspaceVisibility.Public) =>
        await PutAsync($"/api/v1/workspaces/{id}", new
        {
            name,
            description,
            visibility = visibility.ToString()
        });

    protected async Task<BoardDto> CreateBoardAsync(
        Guid workspaceId, string name, BoardVisibility visibility = BoardVisibility.Workspace)
    {
        using var response = await PostAsync("/api/v1/boards", new
        {
            name,
            workspaceId,
            visibility = visibility.ToString()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<BoardDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateBoardAsync(
        Guid id,
        string name,
        string? description = null,
        BoardVisibility visibility = BoardVisibility.Workspace) =>
        PutAsync($"/api/v1/boards/{id}", new
        {
            name,
            description,
            visibility = visibility.ToString()
        });

    protected Task<HttpResponseMessage> MoveBoardAsync(Guid id, Guid workspaceId) =>
        PutAsync($"/api/v1/boards/{id}/workspace", new { workspaceId });

    protected async Task<ListDto> CreateListAsync(
        Guid boardId, string name, Color? color = null, object? position = null)
    {
        using var response = await PostAsync("/api/v1/lists", new
        {
            name,
            boardId,
            color = color?.ToString(),
            position
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<ListDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateListAsync(Guid id, string name, Color? color = null) =>
        PutAsync($"/api/v1/lists/{id}", new { name, color = color?.ToString() });

    protected Task<HttpResponseMessage> MoveListAsync(
        Guid id, Guid boardId, object? position = null, Guid? before = null, Guid? after = null) =>
        PutAsync($"/api/v1/lists/{id}/position", new { boardId, position, before, after });

    protected async Task<ListDto> ReadListAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/lists/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<ListDto>()).Data!;
    }

    protected async Task<CardDto> CreateCardAsync(
        Guid listId,
        string title,
        DateTime? dueDate = null,
        object? position = null,
        IEnumerable<Guid>? labelIds = null,
        bool isDueComplete = false)
    {
        using var response = await PostAsync("/api/v1/cards", new
        {
            title,
            listId,
            dueDate,
            position,
            labelIds,
            isDueComplete
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<CardDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateCardAsync(
        Guid id,
        string title,
        string? description = null,
        DateTime? dueDate = null,
        IEnumerable<Guid>? labelIds = null,
        bool isDueComplete = false) =>
        PutAsync($"/api/v1/cards/{id}", new
        {
            title,
            description,
            dueDate,
            labelIds,
            isDueComplete
        });

    protected Task<HttpResponseMessage> MoveCardAsync(
        Guid id, Guid listId, object? position = null, Guid? before = null, Guid? after = null) =>
        PutAsync($"/api/v1/cards/{id}/position", new { listId, position, before, after });

    protected async Task<CardDto> ReadCardAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/cards/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<CardDto>()).Data!;
    }

    protected async Task AddLabelAsync(Guid cardId, Guid labelId)
    {
        using var response = await PostAsync($"/api/v1/cards/{cardId}/labels/{labelId}", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected async Task RemoveLabelAsync(Guid cardId, Guid labelId)
    {
        using var response = await DeleteAsync($"/api/v1/cards/{cardId}/labels/{labelId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected async Task<LabelDto> CreateLabelAsync(Guid boardId, string? name, Color? color = null)
    {
        using var response = await PostAsync("/api/v1/labels", new
        {
            name,
            boardId,
            color = color?.ToString()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<LabelDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateLabelAsync(Guid id, string? name, Color? color = null) =>
        PutAsync($"/api/v1/labels/{id}", new { name, color = color?.ToString() });

    protected async Task<ChecklistDto> CreateChecklistAsync(Guid cardId, string name, object? position = null)
    {
        using var response = await PostAsync("/api/v1/checklists", new { name, cardId, position });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<ChecklistDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateChecklistAsync(Guid id, string name) =>
        PutAsync($"/api/v1/checklists/{id}", new { name });

    protected Task<HttpResponseMessage> MoveChecklistAsync(
        Guid id, object? position = null, Guid? before = null, Guid? after = null) =>
        PutAsync($"/api/v1/checklists/{id}/position", new { position, before, after });

    protected async Task<CheckItemDto> CreateCheckItemAsync(
        Guid checklistId, string name, bool isChecked = false, object? position = null)
    {
        using var response = await PostAsync("/api/v1/check-items", new
        {
            name,
            checklistId,
            isChecked,
            position
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<CheckItemDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateCheckItemAsync(Guid id, string name) =>
        PutAsync($"/api/v1/check-items/{id}", new { name });

    protected Task<HttpResponseMessage> MoveCheckItemAsync(
        Guid id, Guid checklistId, object? position = null, Guid? before = null, Guid? after = null) =>
        PutAsync($"/api/v1/check-items/{id}/position", new { checklistId, position, before, after });

    protected Task<HttpResponseMessage> SendCheckedAsync(Guid id, bool value) =>
        PutAsync($"/api/v1/check-items/{id}/checked", new { value });

    protected async Task SetCheckedAsync(Guid id, bool value)
    {
        using var response = await SendCheckedAsync(id, value);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected Task SetBoardArchivedAsync(Guid id, bool value) =>
        SetArchivedAsync($"/api/v1/boards/{id}/archived", value);

    protected Task SetListArchivedAsync(Guid id, bool value) =>
        SetArchivedAsync($"/api/v1/lists/{id}/archived", value);

    protected Task SetCardArchivedAsync(Guid id, bool value) =>
        SetArchivedAsync($"/api/v1/cards/{id}/archived", value);

    protected Task SetChecklistArchivedAsync(Guid id, bool value) =>
        SetArchivedAsync($"/api/v1/checklists/{id}/archived", value);

    protected Task<HttpResponseMessage> SendChecklistArchivedAsync(Guid id, bool value) =>
        PutAsync($"/api/v1/checklists/{id}/archived", new { value });

    private async Task SetArchivedAsync(string path, bool value)
    {
        using var response = await PutAsync(path, new { value });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected async Task<AttachmentDto> UploadAttachmentAsync(
        Guid cardId, string fileName = "shot.png", string? name = null)
    {
        using var response = await SendUploadAsync(cardId, fileName, name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<AttachmentDto>()).Data!;
    }

    protected async Task<AttachmentDto> CreateLinkAttachmentAsync(Guid cardId, string url, string? name = null)
    {
        using var response = await PostAsync(AttachmentPath(cardId), new { url, name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<AttachmentDto>()).Data!;
    }

    protected Task<HttpResponseMessage> RenameAttachmentAsync(Guid cardId, Guid id, string name) =>
        PutAsync($"{AttachmentPath(cardId)}/{id}", new { name });

    protected async Task DeleteAttachmentAsync(Guid cardId, Guid id)
    {
        using var response = await DeleteAsync($"{AttachmentPath(cardId)}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected static string CommentPath(Guid cardId) => $"/api/v1/cards/{cardId}/comments";

    protected async Task<ActivityEntryDto> CreateCommentAsync(Guid cardId, string text, Guid? actor = null)
    {
        using var response = await SendCommentAsync(cardId, new { text }, actor);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<ActivityEntryDto>()).Data!;
    }

    protected Task<HttpResponseMessage> SendCommentAsync(Guid cardId, object body, Guid? actor = null) =>
        PostAsync(CommentPath(cardId), body, actor);

    protected Task<HttpResponseMessage> EditCommentAsync(
        Guid cardId, Guid id, object body, Guid? actor = null) =>
        PutAsync($"{CommentPath(cardId)}/{id}", body, actor);

    protected Task<HttpResponseMessage> RemoveCommentAsync(Guid cardId, Guid id, Guid? actor = null) =>
        DeleteAsync($"{CommentPath(cardId)}/{id}", actor);

    protected async Task<T> FromDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var factory = _factory ?? throw new InvalidOperationException("The test host isn't up yet.");
        await using var scope = factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected static string CopyPath(Guid cardId) => $"/api/v1/cards/{cardId}/copies";

    protected Task<HttpResponseMessage> SendCardCopyAsync(Guid id, object body, Guid? actor = null) =>
        PostAsync(CopyPath(id), body, actor);

    protected async Task<CardDto> CopyCardAsync(
        Guid id,
        Guid listId,
        string? title = null,
        IEnumerable<string>? keep = null,
        object? position = null,
        Guid? before = null,
        Guid? after = null)
    {
        using var response = await SendCardCopyAsync(
            id, new { listId, title, keep, position, before, after });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<CardDto>()).Data!;
    }

    protected async Task<List<ChecklistDto>> ReadChecklistsAsync(Guid cardId)
    {
        using var response = await GetAsync($"/api/v1/checklists?cardId={cardId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ChecklistDto>()).Entries;
    }

    protected async Task<List<AttachmentDto>> ReadAttachmentsAsync(Guid cardId)
    {
        using var response = await GetAsync($"{AttachmentPath(cardId)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<List<AttachmentDto>>()).Data!;
    }

    protected async Task<byte[]> DownloadAttachmentAsync(Guid cardId, Guid id)
    {
        using var response = await NoRedirects.GetAsync($"{AttachmentPath(cardId)}/{id}/content");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var storage = new HttpClient();
        return await storage.GetByteArrayAsync(response.Headers.Location);
    }

    protected async Task<ApiPage<CardDto>> ReadCardsAsync(string query)
    {
        using var response = await GetAsync($"/api/v1/cards?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadPage<CardDto>();
    }

    protected async Task DeleteCardAsync(Guid id)
    {
        await SetCardArchivedAsync(id, true);
        using var response = await DeleteAsync($"/api/v1/cards/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected Task<HttpResponseMessage> SendCoverAsync(Guid cardId, Guid? attachmentId = null, Color? color = null) =>
        PutAsync($"/api/v1/cards/{cardId}/cover", new { attachmentId, color = color?.ToString() });

    protected async Task SetCoverAsync(Guid cardId, Guid? attachmentId = null, Color? color = null)
    {
        using var response = await SendCoverAsync(cardId, attachmentId, color);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendUploadAsync(Guid cardId, string fileName, string? name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AttachmentPath(cardId));
        var form = new MultipartFormDataContent();
        if (name is not null)
        {
            form.Add(new StringContent(name), "name");
        }

        form.Add(new ByteArrayContent(FileBytes(fileName)), "file", fileName);
        request.Content = form;
        request.Headers.Add(ActorFilter.HeaderName, ActorId.ToString());

        return await Client.SendAsync(request);
    }

    private static byte[] FileBytes(string fileName) =>
        Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A, 0x1A, 0x0A]
            : "A line of notes.\n"u8.ToArray();

    private static string AttachmentPath(Guid cardId) => $"/api/v1/cards/{cardId}/attachments";

    protected async Task<ApiPage<ActivityEntryDto>> ActivityAsync(string query = "")
    {
        using var response = await GetAsync(ActivityPath(query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadPage<ActivityEntryDto>();
    }

    protected async Task<List<ApiError>> ActivityErrorsAsync(string query)
    {
        using var response = await GetAsync(ActivityPath(query));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.ReadErrors();
    }

    protected static string Moment(DateTime createdAt) =>
        Uri.EscapeDataString(createdAt.ToString("O", CultureInfo.InvariantCulture));

    private static string ActivityPath(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Length == 0 ? "/api/v1/activity" : $"/api/v1/activity?{query}";
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string? actorHeader)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (actorHeader is not null)
        {
            request.Headers.Add(ActorFilter.HeaderName, actorHeader);
        }

        return await Client.SendAsync(request);
    }
}
