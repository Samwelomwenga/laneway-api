using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Laneway.Api.Tests;

public abstract class ApiTests : IAsyncLifetime
{
    private readonly TestStack _stack;

    private ApiFactory? _factory;
    private HttpClient? _client;
    private HttpClient? _noRedirects;
    private HttpClient? _https;
    private Guid? _databaseId;

    private readonly List<HttpClient> _hostClients = [];

    protected ApiTests(TestStack stack) => _stack = stack;

    private static readonly Lazy<string> SeededPasswordHash = new(() => new Passwords().Hash(TestUser.Password));

    protected TestUser ActorUser { get; private set; } = null!;

    protected Guid ActorId => ActorUser.Id;

    protected FakeTimeProvider Time => Factory.Time;

    internal PwnedPasswordsStub PwnedPasswords => Factory.PwnedPasswords;

    private ApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The test host isn't up yet.");

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host isn't up yet.");

    private HttpClient NoRedirects =>
        _noRedirects ??= (_factory ?? throw new InvalidOperationException("The test host isn't up yet."))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected HttpClient Https =>
        _https ??= Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

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
        _https?.Dispose();
        _hostClients.ForEach(client => client.Dispose());
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_databaseId is { } databaseId)
        {
            await _stack.DropDatabaseAsync(databaseId);
        }
    }

    protected Task<HttpResponseMessage> PostAsync(string path, object body, TestUser? @as = null) =>
        SendAsync(HttpMethod.Post, path, body, @as ?? ActorUser);

    protected Task<HttpResponseMessage> PutAsync(string path, object body, TestUser? @as = null) =>
        SendAsync(HttpMethod.Put, path, body, @as ?? ActorUser);

    protected Task<HttpResponseMessage> DeleteAsync(string path, TestUser? @as = null) =>
        SendAsync(HttpMethod.Delete, path, null, @as ?? ActorUser);

    protected Task<HttpResponseMessage> SendAnonymousAsync(
        HttpMethod method, string path, object? body, string? userAgent = null) =>
        SendAsync(method, path, body, user: null, userAgent);

    protected const string SignUpPassword = "plum tractor ocean";

    protected Task<HttpResponseMessage> SignUpAsync(object body) =>
        SendFromBrowserAsync("/api/v1/auth/sign-up", body);

    protected static Dictionary<string, string> BrowserHeaders() => new(StringComparer.OrdinalIgnoreCase)
    {
        [CsrfCheck.Header] = "1",
        ["Origin"] = ApiFactory.AllowedOrigin,
        ["Sec-Fetch-Site"] = "same-site"
    };

    protected async Task<HttpResponseMessage> SendFromBrowserAsync(
        string path, object? body, IReadOnlyDictionary<string, string>? headers = null, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body);
        }

        AddHeaders(request, headers ?? BrowserHeaders());
        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return await Client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> PostOverHttpsAsync(string path, object body, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        AddHeaders(request, BrowserHeaders());
        if (userAgent is not null)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        return await Https.SendAsync(request);
    }

    private static void AddHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    protected static object SignUpBody(string email, string password = SignUpPassword, string? username = null) => new
    {
        username = username ?? $"ana-{Guid.NewGuid():n}"[..20],
        email,
        password,
        firstName = "Ana",
        lastName = "Waweru",
        language = "en-US",
        timeZone = "UTC",
        location = "Nairobi"
    };

    protected static string NewEmail() => $"{Guid.NewGuid():n}@example.com";

    protected static Guid SessionOf(string accessToken) =>
        Guid.Parse(new JsonWebToken(accessToken).GetPayloadValue<string>(AccessTokens.SessionClaim));

    protected async Task<(SignedUpDto SignedUp, string Cookie)> SignUpOverHttpsAsync()
    {
        using var response = await PostOverHttpsAsync("/api/v1/auth/sign-up", SignUpBody(NewEmail()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.ReadEnvelope<SignedUpDto>()).Data!,
            RefreshCookieAssert.Set(response, Session.AbsoluteLifetime));
    }

    protected async Task<HttpResponseMessage> RefreshAsync(
        string? cookie, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        AddHeaders(request, headers ?? BrowserHeaders());
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieAssert.Name}={cookie}");
        }

        return await Https.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> SendOverHttpsAsync(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await Https.SendAsync(request);
    }

    protected async Task WaitForLockWaitAsync()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var waiting = await FromDatabaseAsync(database => database.Database
                .SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM pg_stat_activity
                    WHERE wait_event_type = 'Lock' AND datname = current_database()
                    """)
                .SingleAsync());
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("The request never waited on the held row lock.");
    }

    protected Task<HttpResponseMessage> GetAsync(string path, TestUser? @as = null) =>
        SendAsync(HttpMethod.Get, path, null, @as ?? ActorUser);

    protected async Task<TestUser> CreateUserAsync()
    {
        var tag = Guid.NewGuid().ToString("n")[..12];
        var now = Time.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"actor-{tag}",
            Email = $"{tag}@example.com",
            PasswordHash = SeededPasswordHash.Value,
            FirstName = "Ana",
            LastName = "Waweru",
            Language = "en-US",
            TimeZone = "UTC",
            Location = "Nairobi",
            CreatedAt = now
        };
        user.CreatedBy = user.Id;
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + Session.AbsoluteLifetime,
            UserAgent = "Laneway.Api.Tests"
        };

        await FromDatabaseAsync(async database =>
        {
            database.Users.Add(user);
            database.Accounts.Add(new Account
            {
                Id = Guid.NewGuid(),
                Name = $"{user.Username}'s Account",
                UserId = user.Id,
                CreatedAt = now,
                CreatedBy = user.Id
            });
            database.Sessions.Add(session);
            return await database.SaveChangesAsync();
        });

        var token = await IssueTokenAsync(user.Id, session.Id);
        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/profile", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return new TestUser((await response.ReadEnvelope<UserDto>()).Data!, session.Id, token);
    }

    protected Task<string> IssueTokenAsync(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return IssueTokenAsync(user.Id, user.SessionId);
    }

    private Task<string> IssueTokenAsync(Guid userId, Guid sessionId) =>
        FromServicesAsync(services =>
            Task.FromResult(services.GetRequiredService<AccessTokens>().Issue(userId, sessionId).Value));

    protected IServiceProvider Services => Factory.Services;

    protected IReadOnlyList<FakeLogRecord> Logs() => Factory.Services.GetFakeLogCollector().GetSnapshot();

    protected (HttpClient Client, FakeLogCollector Collector) HostWith(Action<IServiceCollection> services)
    {
        var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services));
        var client = host.CreateClient();
        _hostClients.Add(client);
        return (client, host.Services.GetFakeLogCollector());
    }

    protected (HttpClient Client, IServiceProvider Services) ProductionHost(
        WebApplicationFactoryClientOptions options, Action<IWebHostBuilder>? configure = null)
    {
        var host = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Only Development loads the API's user secrets, and attachment storage won't start without them.
            // They go in first, so every setting the test host gives still wins.
            builder.ConfigureAppConfiguration(configuration => configuration.Sources.Insert(
                0, new ConfigurationBuilder().AddUserSecrets(typeof(Program).Assembly, optional: true).Sources[0]));
            configure?.Invoke(builder);
        });
        var client = host.CreateClient(options);
        _hostClients.Add(client);
        return (client, host.Services);
    }

    protected static async Task AssertChallengedAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        var envelope = await response.ReadEnvelope<object>();
        Assert.False(envelope.Success);
        Assert.Equal("Sign in again.", envelope.Message);
        Assert.Equal(401, envelope.StatusCode);
        var error = Assert.Single(envelope.Errors!);
        Assert.Equal(ErrorCodes.NotAuthenticated, error.Code);
        Assert.Equal("Sign in again.", error.Message);
        Assert.Null(error.Field);
    }

    protected async Task<HttpResponseMessage> SendWithTokenAsync(
        HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await Client.SendAsync(request);
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

    protected static string BoardsPath(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/boards";

    protected async Task<BoardDto> CreateBoardAsync(
        Guid workspaceId, string name, BoardVisibility visibility = BoardVisibility.Workspace)
    {
        using var response = await PostAsync(BoardsPath(workspaceId), new
        {
            name,
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

    protected static string ListsPath(Guid boardId) => $"/api/v1/boards/{boardId}/lists";

    protected async Task<ListDto> CreateListAsync(
        Guid boardId, string name, Color? color = null, object? position = null)
    {
        using var response = await PostAsync(ListsPath(boardId), new
        {
            name,
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

    protected async Task<List<ListDto>> ReadListsOfBoardAsync(Guid boardId)
    {
        using var response = await GetAsync($"{ListsPath(boardId)}?archived=Include&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ListDto>()).Entries;
    }

    protected async Task<List<LabelDto>> ReadLabelsOfBoardAsync(Guid boardId, string? query = null)
    {
        using var response = await GetAsync(
            query is null ? $"{LabelsPath(boardId)}?pageSize=100" : $"{LabelsPath(boardId)}?pageSize=100&{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<LabelDto>()).Entries;
    }

    protected async Task<BoardDto> ReadBoardAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<BoardDto>()).Data!;
    }

    protected async Task<ListDto> ReadListAsync(Guid id)
    {
        using var response = await GetAsync($"/api/v1/lists/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<ListDto>()).Data!;
    }

    protected static string CardsPath(Guid listId) => $"/api/v1/lists/{listId}/cards";

    protected async Task<CardDto> CreateCardAsync(
        Guid listId,
        string title,
        DateTime? dueDate = null,
        object? position = null,
        IEnumerable<Guid>? labelIds = null,
        bool isDueComplete = false)
    {
        using var response = await PostAsync(CardsPath(listId), new
        {
            title,
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

    protected static string LabelsPath(Guid boardId) => $"/api/v1/boards/{boardId}/labels";

    protected async Task<LabelDto> CreateLabelAsync(Guid boardId, string? name, Color? color = null)
    {
        using var response = await PostAsync(LabelsPath(boardId), new
        {
            name,
            color = color?.ToString()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<LabelDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateLabelAsync(Guid id, string? name, Color? color = null) =>
        PutAsync($"/api/v1/labels/{id}", new { name, color = color?.ToString() });

    protected static string ChecklistsPath(Guid cardId) => $"/api/v1/cards/{cardId}/checklists";

    protected async Task<ChecklistDto> CreateChecklistAsync(Guid cardId, string name, object? position = null)
    {
        using var response = await PostAsync(ChecklistsPath(cardId), new { name, position });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<ChecklistDto>()).Data!;
    }

    protected Task<HttpResponseMessage> UpdateChecklistAsync(Guid id, string name) =>
        PutAsync($"/api/v1/checklists/{id}", new { name });

    protected Task<HttpResponseMessage> MoveChecklistAsync(
        Guid id, object? position = null, Guid? before = null, Guid? after = null) =>
        PutAsync($"/api/v1/checklists/{id}/position", new { position, before, after });

    protected static string CheckItemsPath(Guid checklistId) => $"/api/v1/checklists/{checklistId}/check-items";

    protected async Task<CheckItemDto> CreateCheckItemAsync(
        Guid checklistId, string name, bool isChecked = false, object? position = null)
    {
        using var response = await PostAsync(CheckItemsPath(checklistId), new
        {
            name,
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

    protected async Task<ActivityEntryDto> CreateCommentAsync(Guid cardId, string text, TestUser? @as = null)
    {
        using var response = await SendCommentAsync(cardId, new { text }, @as);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadEnvelope<ActivityEntryDto>()).Data!;
    }

    protected Task<HttpResponseMessage> SendCommentAsync(Guid cardId, object body, TestUser? @as = null) =>
        PostAsync(CommentPath(cardId), body, @as);

    protected Task<HttpResponseMessage> EditCommentAsync(
        Guid cardId, Guid id, object body, TestUser? @as = null) =>
        PutAsync($"{CommentPath(cardId)}/{id}", body, @as);

    protected Task<HttpResponseMessage> RemoveCommentAsync(Guid cardId, Guid id, TestUser? @as = null) =>
        DeleteAsync($"{CommentPath(cardId)}/{id}", @as);

    protected async Task<T> FromDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var factory = _factory ?? throw new InvalidOperationException("The test host isn't up yet.");
        await using var scope = factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected async Task<T> FromServicesAsync<T>(Func<IServiceProvider, Task<T>> use)
    {
        ArgumentNullException.ThrowIfNull(use);

        var factory = _factory ?? throw new InvalidOperationException("The test host isn't up yet.");
        await using var scope = factory.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider);
    }

    protected static string CopyPath(Guid cardId) => $"/api/v1/cards/{cardId}/copies";

    protected static string ListCopyPath(Guid listId) => $"/api/v1/lists/{listId}/copies";

    protected static string BoardCopyPath(Guid boardId) => $"/api/v1/boards/{boardId}/copies";

    protected static string CopyJobPath(Guid id) => $"/api/v1/copy-jobs/{id}";

    protected Task<HttpResponseMessage> SendCardCopyAsync(Guid id, object body, TestUser? @as = null) =>
        PostAsync(CopyPath(id), body, @as);

    protected Task<HttpResponseMessage> SendListCopyAsync(Guid id, object body, TestUser? @as = null) =>
        PostAsync(ListCopyPath(id), body, @as);

    protected async Task<CopyJobDto> CopyListAsync(
        Guid id,
        Guid boardId,
        string? name = null,
        IEnumerable<string>? keep = null,
        object? position = null,
        Guid? before = null,
        Guid? after = null,
        TestUser? @as = null)
    {
        using var response = await SendListCopyAsync(
            id, new { boardId, name, keep, position, before, after }, @as);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.ReadEnvelope<CopyJobDto>()).Data!;
    }

    protected Task<HttpResponseMessage> SendBoardCopyAsync(Guid id, object body, TestUser? @as = null) =>
        PostAsync(BoardCopyPath(id), body, @as);

    protected async Task<CopyJobDto> CopyBoardAsync(
        Guid id,
        Guid workspaceId,
        string? name = null,
        IEnumerable<string>? keep = null,
        TestUser? @as = null)
    {
        using var response = await SendBoardCopyAsync(id, new { workspaceId, name, keep }, @as);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.ReadEnvelope<CopyJobDto>()).Data!;
    }

    protected async Task<CopyJobDto> ReadCopyJobAsync(Guid id)
    {
        using var response = await GetAsync(CopyJobPath(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<CopyJobDto>()).Data!;
    }

    protected async Task<CopyJobDto> AwaitCopyJobAsync(Guid id, CopyJobStatus status = CopyJobStatus.Succeeded)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var job = await ReadCopyJobAsync(id);
            if (job.Status == status)
            {
                return job;
            }

            if (job.FinishedAt is not null || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException(
                    $"Copy job {id} is {job.Status}, not {status}. Errors: {Described(job.Errors)}");
            }

            await Task.Delay(100);
        }
    }

    protected async Task<List<CardDto>> ReadCardsOfListAsync(Guid listId) =>
        (await ReadCardsAsync(listId, "pageSize=100")).Entries;

    private static string Described(List<ApiError>? errors) =>
        errors is null ? "none" : string.Join("; ", errors.Select(error => $"{error.Field}/{error.Code}"));

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

    protected async Task<List<ChecklistDto>> ReadChecklistsAsync(Guid cardId, string? query = null)
    {
        using var response = await GetAsync(
            query is null ? ChecklistsPath(cardId) : $"{ChecklistsPath(cardId)}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<ChecklistDto>()).Entries;
    }

    protected async Task<List<CheckItemDto>> ReadCheckItemsAsync(Guid checklistId, string? query = null)
    {
        using var response = await GetAsync(
            query is null ? CheckItemsPath(checklistId) : $"{CheckItemsPath(checklistId)}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadPage<CheckItemDto>()).Entries;
    }

    protected async Task<List<AttachmentDto>> ReadAttachmentsAsync(Guid cardId)
    {
        using var response = await GetAsync($"{AttachmentPath(cardId)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<List<AttachmentDto>>()).Data!;
    }

    protected async Task<byte[]> DownloadAttachmentAsync(Guid cardId, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AttachmentPath(cardId)}/{id}/content");
        request.Headers.Authorization = Bearer(ActorUser);
        using var response = await NoRedirects.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var storage = new HttpClient();
        return await storage.GetByteArrayAsync(response.Headers.Location);
    }

    protected async Task<ApiPage<CardDto>> ReadCardsAsync(Guid listId, string? query = null)
    {
        using var response = await GetAsync(query is null ? CardsPath(listId) : $"{CardsPath(listId)}?{query}");
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
        request.Headers.Authorization = Bearer(ActorUser);

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
        HttpMethod method, string path, object? body, TestUser? user, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body);
        }
        if (user is not null)
        {
            request.Headers.Authorization = Bearer(user);
        }
        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return await Client.SendAsync(request);
    }

    private static AuthenticationHeaderValue Bearer(TestUser user) => new("Bearer", user.AccessToken);
}
