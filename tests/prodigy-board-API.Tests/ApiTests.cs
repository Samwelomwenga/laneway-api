using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DefaultNamespace;
using Xunit;

namespace prodigy_board_API.Tests;

public abstract class ApiTests : IAsyncLifetime
{
    private readonly TestStack _stack;

    private ApiFactory? _factory;
    private HttpClient? _client;
    private string? _connectionString;

    protected ApiTests(TestStack stack) => _stack = stack;

    protected UserDto ActorUser { get; private set; } = null!;

    protected Guid ActorId => ActorUser.Id;

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host isn't up yet.");

    public async Task InitializeAsync()
    {
        _connectionString = await _stack.NewDatabaseAsync();
        _factory = new ApiFactory(_connectionString);
        _client = _factory.CreateApiClient();
        ActorUser = await CreateUserAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_connectionString is not null)
        {
            await _stack.DropDatabaseAsync(_connectionString);
        }
    }

    protected Task<HttpResponseMessage> PostAsync(string path, object body) =>
        SendAsync(HttpMethod.Post, path, body, ActorId.ToString());

    protected Task<HttpResponseMessage> PutAsync(string path, object body) =>
        SendAsync(HttpMethod.Put, path, body, ActorId.ToString());

    protected Task<HttpResponseMessage> DeleteAsync(string path) =>
        SendAsync(HttpMethod.Delete, path, null, ActorId.ToString());

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
