using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
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

    internal BrevoStub Brevo => Factory.Brevo;

    internal Shipped<LogRecord> ShippedLogs => Factory.ShippedLogs;

    internal Shipped<Activity> ShippedSpans => Factory.ShippedSpans;

    internal IReadOnlyList<MetricSnapshot> ShippedMetrics()
    {
        Services.GetRequiredService<MeterProvider>().ForceFlush();
        return Factory.ShippedMetrics.Snapshot();
    }

    internal Task<BrevoSend> AwaitEmailAsync(string to) => Brevo.AwaitEmailAsync(to);

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
        List<string> unlisted = [];
        try
        {
            if (_factory is not null)
            {
                unlisted = await StopAndListUnlistedInInventoryAsync(_factory);
            }
        }
        finally
        {
            if (_databaseId is { } databaseId)
            {
                await _stack.DropDatabaseAsync(databaseId);
            }
        }

        Assert.Empty(unlisted);
    }

    private static async Task<List<string>> StopAndListUnlistedInInventoryAsync(ApiFactory factory)
    {
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush();
        await factory.DisposeAsync();

        var logs = factory.ShippedLogs.Select(log =>
        {
            var keys = (log.Attributes ?? []).Select(pair => pair.Key).ToList();
            log.ForEachScope(
                (scope, scopeKeys) =>
                {
                    foreach (var pair in scope)
                    {
                        scopeKeys.Add(pair.Key);
                    }
                },
                keys);
            return new ShippedLog(log.CategoryName ?? string.Empty, log.LogLevel, log.EventId.Name, keys);
        });
        var counters = factory.ShippedMetrics.Snapshot().SelectMany(metric => metric.MetricPoints.Select(point =>
        {
            var keys = new List<string>();
            foreach (var tag in point.Tags)
            {
                keys.Add(tag.Key);
            }

            return new ShippedCounter(metric.MeterName, metric.Name, keys);
        }));
        return LoggingInventory.Documented.Violations(logs, counters);
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

    protected static string EmailOfLength(int length) => new string('a', length - "@example.com".Length) + "@example.com";

    protected static Dictionary<string, object?> Fields(object body) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(body))!;

    protected static Guid SessionOf(string accessToken) =>
        Guid.Parse(new JsonWebToken(accessToken).GetPayloadValue<string>(AccessTokens.SessionClaim));

    protected sealed record SignedUp(
        string AccessToken, DateTime AccessTokenExpiresAt, DateTime SessionExpiresAt, UserDto User);

    protected async Task<(SignedUp SignedUp, string Cookie)> SignUpOverHttpsAsync(
        string? email = null, string password = SignUpPassword)
    {
        email ??= NewEmail();
        using var signUp = await PostOverHttpsAsync("/api/v1/auth/sign-up", SignUpBody(email, password));
        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        using var verify = await VerifyEmailAsync(await VerifyTokenSentToAsync(email));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var signedIn = (await verify.ReadEnvelope<SignedInDto>()).Data!;
        using var profile = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/profile", signedIn.AccessToken);
        var user = (await profile.ReadEnvelope<UserDto>()).Data!;
        return (new SignedUp(signedIn.AccessToken, signedIn.AccessTokenExpiresAt, signedIn.SessionExpiresAt, user),
            RefreshCookieAssert.Set(verify, Session.AbsoluteLifetime));
    }

    protected async Task<string> VerifyTokenSentToAsync(string email) =>
        (await AwaitEmailAsync(Emails.Normalize(email))).TokenFrom(AppRoutes.VerifyEmail);

    protected Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        PostOverHttpsAsync("/api/v1/auth/verify-email", new { token });

    protected Task<HttpResponseMessage> ResendVerificationAsync(string email) =>
        SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/resend-verification", new { email });

    protected Task<HttpResponseMessage> ForgotPasswordAsync(string email) =>
        SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/forgot-password", new { email });

    protected Task<HttpResponseMessage> ResetPasswordAsync(string token, string newPassword) =>
        SendAnonymousAsync(HttpMethod.Post, "/api/v1/auth/reset-password", new { token, newPassword });

    protected Task SuppressAsync(string email) =>
        FromDatabaseAsync(database =>
        {
            database.Suppressions.Add(new Suppression
            {
                AddressHash = Suppressions.Hash(email),
                Kind = SuppressionKind.HardBounce,
                SuppressedAt = Time.GetUtcNow().UtcDateTime,
                MessageId = "<seeded@smtp-relay.mailin.fr>"
            });
            return database.SaveChangesAsync();
        });

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
            CreatedAt = now,
            Verified = true
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

    protected async Task<List<FakeLogRecord>> AwaitLogsAsync(string name, int count)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            var found = Logs().Where(log => log.Id.Name == name).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"{name} didn't log {count} times within 5 seconds.");
    }

    protected void AdvanceDeliveries(TimeSpan by)
    {
        Time.Advance(by);
        Services.GetRequiredService<DeliverySignal>().Poke();
    }

    protected async Task<Delivery> AwaitDeliveryAsync(DeliveryStatus status)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            var found = await FromDatabaseAsync(database =>
                database.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Status == status));
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"No delivery reached {status} within 5 seconds.");
    }

    protected async Task AddMessageAsync(IMessage message)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Messages>().Add(message);
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }

    protected PasswordChanged PasswordNotice(Guid? userId = null) =>
        new(userId ?? ActorId, PasswordChangeHow.Changed, Time.GetUtcNow().UtcDateTime, "Laneway.Api.Tests", "UTC");

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

    protected static string WorkspaceMembersPath(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/members";

    protected static string BoardMembersPath(Guid boardId) => $"/api/v1/boards/{boardId}/members";

    protected static string WorkspaceInvitesPath(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/invites";

    protected static string BoardInvitesPath(Guid boardId) => $"/api/v1/boards/{boardId}/invites";

    protected async Task<TestUser> AddBoardMemberAsync(Guid boardId, BoardRole role)
    {
        var user = await CreateUserAsync();
        using var response = await PostAsync(BoardMembersPath(boardId), new { userId = user.Id, role = role.ToString() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return user;
    }

    protected async Task<TestUser> AddWorkspaceMemberAsync(Guid workspaceId, WorkspaceRole role)
    {
        var user = await CreateUserAsync();
        using var response = await PostAsync(
            WorkspaceMembersPath(workspaceId), new { userId = user.Id, role = role.ToString() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return user;
    }

    protected async Task PutBoardSettingsAsync(
        Guid boardId,
        BoardCommenting commenting = BoardCommenting.Members,
        BoardAddingMembers addingMembers = BoardAddingMembers.Admins,
        bool selfJoin = true,
        BoardVisibility? visibility = null)
    {
        var current = visibility ?? (await ReadBoardAsync(boardId)).Visibility;
        using var response = await PutAsync($"/api/v1/boards/{boardId}/settings", new
        {
            commenting = commenting.ToString(),
            addingMembers = addingMembers.ToString(),
            selfJoin,
            visibility = current.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    protected Task<HttpResponseMessage> UpdateBoardAsync(Guid id, string name, string? description = null) =>
        PutAsync($"/api/v1/boards/{id}", new { name, description });

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

    protected Task<HttpResponseMessage> RenameAttachmentAsync(Guid id, string name) =>
        PutAsync($"/api/v1/attachments/{id}", new { name });

    protected async Task DeleteAttachmentAsync(Guid id)
    {
        using var response = await DeleteAsync($"/api/v1/attachments/{id}");
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

    protected Task<HttpResponseMessage> EditCommentAsync(Guid id, object body, TestUser? @as = null) =>
        PutAsync($"/api/v1/comments/{id}", body, @as);

    protected Task<HttpResponseMessage> RemoveCommentAsync(Guid id, TestUser? @as = null) =>
        DeleteAsync($"/api/v1/comments/{id}", @as);

    protected static async Task AssertLacksAsync(Func<Task<HttpResponseMessage>> send, params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(fields);

        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        var items = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList() : [data];
        Assert.NotEmpty(items);
        foreach (var field in fields)
        {
            Assert.All(items, item => Assert.False(item.Has(field), item.GetRawText()));
        }
    }

    protected async Task<T> FromDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var factory = _factory ?? throw new InvalidOperationException("The test host isn't up yet.");
        await using var scope = factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected Task MigrateWithUsersAsync(string migration, params (Guid Id, string Username, DateTime CreatedAt)[] users) =>
        FromDatabaseAsync(async database =>
        {
            var migrator = database.GetService<IMigrator>();
            var all = database.Database.GetMigrations().ToList();
            await migrator.MigrateAsync(all[all.FindIndex(name => name.EndsWith($"_{migration}", StringComparison.Ordinal)) - 1]);
            foreach (var (id, username, createdAt) in users)
            {
                var email = $"{id:n}@example.com";
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Users" ("Id", "Username", "Email", "PasswordHash", "FirstName", "LastName",
                        "Language", "TimeZone", "Location", "CreatedAt", "CreatedBy", "FailedSignInCount")
                    VALUES ({id}, {username}, {email}, 'hash', 'Ana', 'Waweru', 'en-US', 'UTC', 'Nairobi', {createdAt}, {id}, 0)
                    """);
            }

            await migrator.MigrateAsync();
            return 0;
        });

    protected Task<List<Guid>> SeedCardsAsync(Guid listId, int count, bool archived = false) =>
        SeedAsync(context => context.Cards, count, n => new Card
        {
            Id = Guid.NewGuid(),
            Title = $"Seeded card {n}",
            ListId = listId,
            Position = n * Placements.Gap,
            IsArchived = archived,
            CreatedBy = ActorId
        });

    protected Task<List<Guid>> SeedListsAsync(Guid boardId, int count, bool archived = false) =>
        SeedAsync(context => context.Lists, count, n => new List
        {
            Id = Guid.NewGuid(),
            Name = $"Seeded list {n}",
            BoardId = boardId,
            Position = n * Placements.Gap,
            IsArchived = archived,
            CreatedBy = ActorId
        });

    protected Task<List<Guid>> SeedLabelsAsync(Guid boardId, int count) =>
        SeedAsync(context => context.Labels, count, n => new Label
        {
            Id = Guid.NewGuid(),
            Name = $"Seeded label {n}",
            BoardId = boardId,
            CreatedBy = ActorId
        });

    private Task<List<Guid>> SeedAsync<T>(Func<ApplicationDbContext, DbSet<T>> set, int count, Func<int, T> row)
        where T : BaseEntity =>
        FromDatabaseAsync(async context =>
        {
            var rows = Enumerable.Range(1, count).Select(row).ToList();
            set(context).AddRange(rows);
            await context.SaveChangesAsync();
            return rows.ConvertAll(seeded => seeded.Id);
        });

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
        using var response = await GetAsync(AttachmentPath(cardId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<List<AttachmentDto>>()).Data!;
    }

    protected async Task<HttpResponseMessage> GetWithoutRedirectAsync(string path, TestUser user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = Bearer(user);
        return await NoRedirects.SendAsync(request);
    }

    protected async Task<byte[]> DownloadAttachmentAsync(Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attachments/{id}/content");
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
