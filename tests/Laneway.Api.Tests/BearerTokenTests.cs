using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BearerTokenTests : ApiTests
{
    public BearerTokenTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_write_with_a_token_records_the_tokens_user_as_actor()
    {
        var ben = await SeedUserAsync();

        using var response = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", ben.AccessToken, new { name = "Design", visibility = "Public" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var workspace = (await response.ReadEnvelope<WorkSpaceDto>()).Data!;
        Assert.Equal(ben.Id, workspace.CreatedBy);
    }

    [Fact]
    public async Task A_read_with_a_token_works()
    {
        var ben = await SeedUserAsync();
        var workspace = await CreateWorkspaceAsync("Design");

        using var response = await SendWithTokenAsync(
            HttpMethod.Get, $"/api/v1/workspaces/{workspace.Id}", ben.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_issued_token_carries_only_the_agreed_header_and_claims()
    {
        var ben = await SeedUserAsync();

        var token = new JsonWebToken(ben.AccessToken);

        Assert.Equal("HS256", token.Alg);
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal(ApiFactory.SigningKeyId, token.Kid);
        Assert.Equal(
            ["aud", "exp", "iat", "iss", "sid", "sub"],
            token.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
        Assert.Equal(ben.Id.ToString(), token.Subject);
        Assert.Equal(ben.SessionId.ToString(), token.GetClaim("sid").Value);
        Assert.Equal(ApiFactory.Audience, Assert.Single(token.Audiences));
        Assert.Equal(ApiFactory.Issuer, token.Issuer);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task A_token_within_the_clock_skew_past_its_expiry_still_works()
    {
        var ben = await SeedUserAsync();
        Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(20));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", ben.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_gets_401()
    {
        var ben = await SeedUserAsync();
        Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(31));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", ben.AccessToken);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_token_for_an_ended_session_gets_401()
    {
        var ben = await SeedUserAsync();
        await EndSessionAsync(ben.SessionId);

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", ben.AccessToken);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_token_for_a_session_idle_past_seven_days_gets_401()
    {
        var ben = await SeedUserAsync();
        Time.Advance(TimeSpan.FromDays(7));
        var token = await IssueTokenAsync(ben);

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", token);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_token_for_a_deleted_user_gets_401()
    {
        var ben = await SeedUserAsync();
        await FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == ben.Id)
            .ExecuteUpdateAsync(user => user.SetProperty(u => u.DeletedAt, Time.GetUtcNow().UtcDateTime)));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", ben.AccessToken);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_token_with_an_unknown_kid_gets_401_and_logs_no_kid()
    {
        var ben = await SeedUserAsync();
        var forged = Forge(ben, new SymmetricSecurityKey(Convert.FromBase64String(OtherKey)) { KeyId = "stolen" });

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", forged);

        await AssertNotAuthenticatedAsync(response);
        var rejected = Assert.Single(Logs(), log => log.Id.Name == "AccessTokenRejected");
        Assert.Equal("Laneway.Auth", rejected.Category);
        Assert.Equal(LogLevel.Information, rejected.Level);
        Assert.Equal("False", rejected.GetStructuredStateValue("kidKnown"));
        Assert.Null(rejected.GetStructuredStateValue("kid"));
    }

    [Fact]
    public async Task A_rejected_token_with_a_configured_kid_logs_the_kid()
    {
        var ben = await SeedUserAsync();
        Time.Advance(TimeSpan.FromMinutes(16));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", ben.AccessToken);

        await AssertNotAuthenticatedAsync(response);
        var rejected = Assert.Single(Logs(), log => log.Id.Name == "AccessTokenRejected");
        Assert.Equal(ApiFactory.SigningKeyId, rejected.GetStructuredStateValue("kid"));
        Assert.Equal("expired", rejected.GetStructuredStateValue("reason"));
    }

    [Fact]
    public async Task A_token_with_the_wrong_typ_gets_401()
    {
        var ben = await SeedUserAsync();
        var forged = Forge(ben, TestKey(), type: "JWT");

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", forged);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task An_unsigned_token_gets_401()
    {
        var ben = await SeedUserAsync();
        var forged = Resigned(Forge(ben, TestKey()), "none", _ => []);

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", forged);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_token_signed_with_the_right_key_and_the_wrong_alg_gets_401()
    {
        var ben = await SeedUserAsync();
        var key = Convert.FromBase64String(ApiFactory.SigningKey);
        var forged = Resigned(Forge(ben, TestKey()), "HS512", input => HMACSHA512.HashData(key, input));

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", forged);

        await AssertNotAuthenticatedAsync(response);
    }

    [Fact]
    public async Task A_malformed_token_gets_401()
    {
        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", "not-a-token");

        await AssertNotAuthenticatedAsync(response);
        var rejected = Assert.Single(Logs(), log => log.Id.Name == "AccessTokenRejected");
        Assert.Equal("malformed", rejected.GetStructuredStateValue("reason"));
    }

    [Fact]
    public async Task A_correctly_forged_token_is_accepted()
    {
        var ben = await SeedUserAsync();
        var forged = Forge(ben, TestKey());

        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/workspaces", forged);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_write_ignores_the_user_id_header()
    {
        var ben = await SeedUserAsync();

        using var response = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", ben.AccessToken, new { name = "Design", visibility = "Public" },
            userIdHeader: ActorId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ben.Id, (await response.ReadEnvelope<WorkSpaceDto>()).Data!.CreatedBy);
    }

    private const string OtherKey = "sPrCzru4irSfWucz67ISqUecyHq28c+ty6TQ0jhRkdQ=";

    private static SymmetricSecurityKey TestKey() =>
        new(Convert.FromBase64String(ApiFactory.SigningKey)) { KeyId = ApiFactory.SigningKeyId };

    private string Forge(TestUser user, SecurityKey key, string type = "at+jwt")
    {
        var now = Time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = ApiFactory.Issuer,
                Audience = ApiFactory.Audience,
                IssuedAt = now,
                Expires = now.AddMinutes(15),
                TokenType = type,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
                Claims = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["sub"] = user.Id.ToString(),
                    ["sid"] = user.SessionId.ToString()
                }
            });
    }

    private static string Resigned(string token, string algorithm, Func<byte[], byte[]> sign)
    {
        var payload = token.Split('.')[1];
        var header = Base64UrlEncoder.Encode(
            $$"""{"alg":"{{algorithm}}","typ":"at+jwt","kid":"{{ApiFactory.SigningKeyId}}"}""");
        var signature = Base64UrlEncoder.Encode(sign(Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    private Task EndSessionAsync(Guid sessionId) =>
        FromDatabaseAsync(database => database.Sessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(session => session
                .SetProperty(s => s.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(s => s.EndReason, SessionEndReason.SignedOut)));

    private static async Task AssertNotAuthenticatedAsync(HttpResponseMessage response)
    {
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
}

public class SigningKeyTests
{
    private const string Key = ApiFactory.SigningKey;

    [Fact]
    public void An_empty_key_list_fails()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => SigningKeys.From([]));

        Assert.Contains("Auth:SigningKeys", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_under_32_bytes_fails()
    {
        var shortKey = Convert.ToBase64String(new byte[31]);

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            SigningKeys.From([new SigningKeyOptions { Id = "2026-10", Key = shortKey }]));

        Assert.Contains("2026-10", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_is_not_base64_fails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SigningKeys.From([new SigningKeyOptions { Id = "2026-10", Key = "not base64!" }]));
    }

    [Fact]
    public void A_repeated_id_fails()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => SigningKeys.From(
        [
            new SigningKeyOptions { Id = "2026-10", Key = Key },
            new SigningKeyOptions { Id = "2026-10", Key = Convert.ToBase64String(new byte[32]) }
        ]));

        Assert.Contains("2026-10", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_with_no_id_fails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SigningKeys.From([new SigningKeyOptions { Id = " ", Key = Key }]));
    }

    [Fact]
    public void Key_zero_signs_and_every_key_validates()
    {
        var keys = SigningKeys.From(
        [
            new SigningKeyOptions { Id = "2026-11", Key = Key },
            new SigningKeyOptions { Id = "2026-10", Key = Convert.ToBase64String(new byte[32]) }
        ]);

        Assert.Equal("2026-11", keys.Signing.Key.KeyId);
        Assert.Equal(SecurityAlgorithms.HmacSha256, keys.Signing.Algorithm);
        Assert.Equal(["2026-11", "2026-10"], keys.All.Select(key => key.KeyId));
    }
}
