using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public partial class AnonymousAccessTests : ApiTests
{
    private static readonly WebApplicationFactoryClientOptions OverHttps = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    };

    public AnonymousAccessTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Only_sign_up_sign_in_and_refresh_take_an_anonymous_request()
    {
        var (client, services) = ProductionHost(OverHttps);
        var routes = Routes(services);
        var accepted = new List<string>();
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), Filled(route.Template));
            using var response = await client.SendAsync(request);
            if (response.Headers.WwwAuthenticate.Count == 0)
            {
                accepted.Add(route.ToString());
            }
            else
            {
                await AssertChallengedAsync(response);
            }
        }

        Assert.True(routes.Count > 50, $"Only {routes.Count} routes were found.");
        Assert.Equal(
            ["POST /api/v1/auth/refresh", "POST /api/v1/auth/sign-in", "POST /api/v1/auth/sign-up"],
            accepted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_read_needs_a_token()
    {
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await SendAnonymousAsync(HttpMethod.Get, $"/api/v1/workspaces/{workspace.Id}", null);

        await AssertChallengedAsync(response);
    }

    [Fact]
    public async Task The_user_id_header_means_nothing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces")
        {
            Content = JsonContent.Create(new { name = "Design", visibility = "Public" })
        };
        request.Headers.Add("X-User-Id", ActorId.ToString());

        using var response = await Https.SendAsync(request);

        await AssertChallengedAsync(response);
    }

    [Fact]
    public async Task Users_cannot_be_created_outside_sign_up()
    {
        using var response = await PostAsync("/api/v1/users", SignUpBody(NewEmail()));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task The_development_openapi_document_is_anonymous_and_declares_a_bearer_scheme()
    {
        using var response = await SendAnonymousAsync(HttpMethod.Get, "/openapi/v1.json", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.False(paths.GetProperty("/api/v1/auth/sign-in").GetProperty("post").TryGetProperty("security", out _));
        var security = paths.GetProperty("/api/v1/accounts").GetProperty("get").GetProperty("security");
        Assert.True(Assert.Single(security.EnumerateArray()).TryGetProperty("Bearer", out _));
    }

    [Fact]
    public async Task The_development_api_reference_is_anonymous()
    {
        using var response = await SendAnonymousAsync(HttpMethod.Get, "/scalar/v1", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_root_redirects_to_the_api_reference_in_development()
    {
        using var response = await SendAnonymousAsync(HttpMethod.Get, "/", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/scalar/v1", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task The_root_is_not_mapped_outside_development()
    {
        var (client, services) = ProductionHost(OverHttps);
        var token = services.GetRequiredService<AccessTokens>().Issue(ActorId, ActorUser.SessionId).Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Authorization = new("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_caller_the_policy_refuses_gets_403_forbidden()
    {
        var (client, _) = HostWith(services => services.AddSingleton<IAuthorizationHandler>(new RefuseEveryone()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workspaces");
        request.Headers.Authorization = new("Bearer", ActorUser.AccessToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        var envelope = await response.ReadEnvelope<object>();
        Assert.False(envelope.Success);
        Assert.Equal(403, envelope.StatusCode);
        var error = Assert.Single(envelope.Errors!);
        Assert.Equal(ErrorCodes.Forbidden, error.Code);
        Assert.Null(error.Field);
    }

    [Fact]
    public void An_unresolved_actor_says_the_request_is_not_authenticated()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Actor().Id);

        Assert.Contains("isn't authenticated", error.Message, StringComparison.Ordinal);
    }

    private static List<Route> Routes(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new Route(method, "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct()
            .ToList();

    private static string Filled(string template) => Parameter().Replace(template, Guid.NewGuid().ToString());

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    private sealed record Route(string Method, string Template)
    {
        public override string ToString() => $"{Method} {Template}";
    }

    private sealed class RefuseEveryone : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Fail();
            return Task.CompletedTask;
        }
    }
}
