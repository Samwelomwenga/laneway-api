using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class BrevoWebhookTests : ApiTests
{
    private const string Path = "/api/v1/webhooks/brevo";

    public BrevoWebhookTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_hard_bounce_stops_later_mail_to_the_address()
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event("hard_bounce", send.To, send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AddMessageAsync(PasswordNotice());
        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Single(Brevo.Sends);
        var dropped = Assert.Single(await AwaitLogsAsync("DeliveryDropped", 1));
        Assert.Equal("suppressed", dropped.GetStructuredStateValue("reason"));
        Assert.Equal("hardBounce", dropped.GetStructuredStateValue("suppressedBy"));
    }

    [Theory]
    [InlineData("spam", "spam")]
    [InlineData("blocked", "blocked")]
    public async Task A_spam_complaint_or_a_block_stops_later_mail_too(string brevoEvent, string suppressedBy)
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event(brevoEvent, send.To, send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AddMessageAsync(PasswordNotice());
        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Single(Brevo.Sends);
        var dropped = Assert.Single(await AwaitLogsAsync("DeliveryDropped", 1));
        Assert.Equal(suppressedBy, dropped.GetStructuredStateValue("suppressedBy"));
    }

    [Fact]
    public async Task An_unsubscribe_doesnt_stop_a_message_that_isnt_opt_out()
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event("unsubscribed", send.To, send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var suppressed = Assert.Single(await AwaitLogsAsync("AddressSuppressed", 1));
        Assert.Equal("unsubscribed", suppressed.GetStructuredStateValue("event"));
        await AddMessageAsync(PasswordNotice());
        await Brevo.AwaitSendsAsync(2);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "DeliveryDropped");
    }

    [Fact]
    public async Task A_suppression_logs_the_delivery_and_counts_the_event()
    {
        using var suppressions = new MetricCollector<long>(
            Services.GetRequiredService<IMeterFactory>(), MessageMeter.Name, MessageMeter.SuppressionsName);
        var send = await SendNoticeAsync();
        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Sent);

        using var response = await PostEventAsync(Event("hard_bounce", send.To, send.MessageId));

        var suppressed = Assert.Single(Logs(), log => log.Id.Name == "AddressSuppressed");
        Assert.Equal(MessageLog.Category, suppressed.Category);
        Assert.Equal(LogLevel.Information, suppressed.Level);
        Assert.Equal("hardBounce", suppressed.GetStructuredStateValue("event"));
        Assert.Equal(delivery.Id.ToString(), suppressed.GetStructuredStateValue("deliveryId"));
        Assert.Equal("PasswordChanged", suppressed.GetStructuredStateValue("messageType"));
        Assert.Equal("False", suppressed.GetStructuredStateValue("alreadySuppressed"));
        Assert.Equal(["hardBounce"], suppressions.GetMeasurementSnapshot().Select(m => m.Tags["event"]));
    }

    [Fact]
    public async Task A_suppression_stores_only_the_address_hash_the_kind_the_time_and_the_message_id()
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event("spam", send.To, send.MessageId));

        var suppression = await FromDatabaseAsync(database => database.Suppressions.AsNoTracking().SingleAsync());
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(ActorUser.User.Email.ToLowerInvariant())), suppression.AddressHash);
        Assert.Equal(SuppressionKind.Spam, suppression.Kind);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, suppression.SuppressedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(send.MessageId, suppression.MessageId);
        Assert.Equal(
            ["AddressHash", "Kind", "MessageId", "SuppressedAt"],
            Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>().Model
                .FindEntityType(typeof(Suppression))!.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_address_matches_whatever_its_case_and_spacing()
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event("hard_bounce", $"  {send.To.ToUpperInvariant()} ", send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AwaitLogsAsync("AddressSuppressed", 1);
    }

    [Fact]
    public async Task A_repeated_event_writes_nothing_new()
    {
        using var suppressions = new MetricCollector<long>(
            Services.GetRequiredService<IMeterFactory>(), MessageMeter.Name, MessageMeter.SuppressionsName);
        var send = await SendNoticeAsync();
        (await PostEventAsync(Event("hard_bounce", send.To, send.MessageId))).Dispose();

        using var response = await PostEventAsync(Event("hard_bounce", send.To, send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var repeated = (await AwaitLogsAsync("AddressSuppressed", 2))[1];
        Assert.Equal("True", repeated.GetStructuredStateValue("alreadySuppressed"));
        Assert.Equal(1, await FromDatabaseAsync(database => database.Suppressions.CountAsync()));
        Assert.Single(suppressions.GetMeasurementSnapshot());
    }

    [Theory]
    [InlineData("unknownMessageId")]
    [InlineData("addressMismatch")]
    [InlineData("missingMessageId")]
    public async Task An_unmatched_event_changes_nothing_and_still_answers_2xx(string reason)
    {
        var send = await SendNoticeAsync();
        var payload = reason switch
        {
            "unknownMessageId" => Event("hard_bounce", send.To, $"<{Guid.NewGuid():n}@smtp-relay.mailin.fr>"),
            "addressMismatch" => Event("hard_bounce", NewEmail(), send.MessageId),
            _ => Event("hard_bounce", send.To, messageId: null)
        };

        using var response = await PostEventAsync(payload);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var unmatched = Assert.Single(Logs(), log => log.Id.Name == "WebhookEventUnmatched");
        Assert.Equal(LogLevel.Warning, unmatched.Level);
        Assert.Equal("hardBounce", unmatched.GetStructuredStateValue("event"));
        Assert.Equal(reason, unmatched.GetStructuredStateValue("reason"));
        Assert.Equal(0, await FromDatabaseAsync(database => database.Suppressions.CountAsync()));
    }

    [Theory]
    [InlineData("delivered", "delivered")]
    [InlineData("soft_bounce", "softBounce")]
    [InlineData("unique_proxy_open", "uniqueProxyOpen")]
    [InlineData("hardBounce", "other")]
    [InlineData("something_new", "other")]
    public async Task An_event_the_webhook_doesnt_handle_is_ignored(string brevoEvent, string logged)
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event(brevoEvent, send.To, send.MessageId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var ignored = Assert.Single(Logs(), log => log.Id.Name == "WebhookEventIgnored");
        Assert.Equal(LogLevel.Information, ignored.Level);
        Assert.Equal(logged, ignored.GetStructuredStateValue("event"));
        Assert.Equal(0, await FromDatabaseAsync(database => database.Suppressions.CountAsync()));
    }

    [Fact]
    public async Task A_body_that_isnt_json_is_ignored()
    {
        using var response = await PostEventAsync(new StringContent("not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("other", Assert.Single(Logs(), log => log.Id.Name == "WebhookEventIgnored").GetStructuredStateValue("event"));
    }

    [Fact]
    public async Task The_webhook_logs_no_address_message_id_or_payload_text()
    {
        var send = await SendNoticeAsync();
        var token = WebhookToken();
        var before = Logs().Count;

        (await PostEventAsync(Event("hard_bounce", send.To, send.MessageId))).Dispose();
        (await PostEventAsync(Event("spam", NewEmail(), send.MessageId))).Dispose();
        (await PostEventAsync(Event("delivered", send.To, send.MessageId))).Dispose();
        (await PostEventAsync(Event("blocked", send.To, send.MessageId), "not-a-token")).Dispose();

        var everything = string.Join('\n', Logs().Skip(before).Select(log =>
            $"{log.Message} {string.Join(' ', log.StructuredState?.Select(pair => pair.Value) ?? [])}"));
        Assert.Contains("AddressSuppressed", Logs().Skip(before).Select(log => log.Id.Name));
        Assert.DoesNotContain(send.To, everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(send.MessageId, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Recipient address rejected", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("185.41.28.109", everything, StringComparison.Ordinal);
        Assert.DoesNotContain(token, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_minted_token_lasts_a_year()
    {
        var token = new JsonWebToken(WebhookToken());

        Assert.Equal(TimeSpan.FromDays(365), token.ValidTo - token.ValidFrom);
        Assert.Equal(["brevo-webhook"], token.Audiences);
        Assert.Equal("HS256", token.Alg);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("noNotBefore")]
    [InlineData("expired")]
    [InlineData("notYetValid")]
    [InlineData("invalidSignature")]
    [InlineData("invalidAlgorithm")]
    [InlineData("invalidAudience")]
    public async Task A_bad_bearer_gets_401_and_changes_nothing(string reason)
    {
        var send = await SendNoticeAsync();
        var now = Time.GetUtcNow().UtcDateTime;
        var bearer = reason switch
        {
            "missing" => null,
            "malformed" => "not.a.jwt",
            "noNotBefore" => Token(BrevoWebhookTokens.Audience, now, notBefore: false),
            "expired" => BrevoWebhookTokens.Mint(ApiFactory.WebhookSigningKey, now - TimeSpan.FromDays(366)),
            "notYetValid" => BrevoWebhookTokens.Mint(ApiFactory.WebhookSigningKey, now + TimeSpan.FromHours(1)),
            "invalidSignature" => BrevoWebhookTokens.Mint("a-different-key-that-is-also-long-enough", now),
            "invalidAlgorithm" => Hs512Token(now),
            _ => Token("laneway-api", now)
        };

        using var response = await PostEventAsync(Event("hard_bounce", send.To, send.MessageId), bearer);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        var rejected = Assert.Single(Logs(), log => log.Id.Name == "WebhookRejected");
        Assert.Equal(MessageLog.Category, rejected.Category);
        Assert.Equal(LogLevel.Warning, rejected.Level);
        Assert.Equal(reason == "noNotBefore" ? "malformed" : reason, rejected.GetStructuredStateValue("reason"));
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "AccessTokenRejected");
        Assert.Equal(0, await FromDatabaseAsync(database => database.Suppressions.CountAsync()));
    }

    [Fact]
    public async Task An_access_token_doesnt_pass_as_a_webhook_token()
    {
        var send = await SendNoticeAsync();

        using var response = await PostEventAsync(Event("hard_bounce", send.To, send.MessageId), ActorUser.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(Logs(), log => log.Id.Name == "WebhookRejected");
    }

    [Fact]
    public async Task A_webhook_token_doesnt_pass_as_an_access_token()
    {
        using var response = await SendWithTokenAsync(HttpMethod.Get, "/api/v1/profile", WebhookToken());

        await AssertChallengedAsync(response);
    }

    private async Task<BrevoSend> SendNoticeAsync()
    {
        await AddMessageAsync(PasswordNotice());
        var send = await AwaitEmailAsync(ActorUser.User.Email);
        await AwaitDeliveryAsync(DeliveryStatus.Sent);
        return send;
    }

    private static Dictionary<string, object?> Event(string name, string? email, string? messageId)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["event"] = name,
            ["id"] = 1234567,
            ["date"] = "2026-10-23 18:05:01",
            ["ts"] = 1792775101,
            ["subject"] = "Your Laneway password was changed",
            ["tags"] = new[] { "PasswordChanged" },
            ["sending_ip"] = "185.41.28.109",
            ["reason"] = $"550 5.1.1 {email}: Recipient address rejected"
        };
        if (email is not null)
        {
            payload["email"] = email;
        }

        if (messageId is not null)
        {
            payload["message-id"] = messageId;
        }

        return payload;
    }

    private static string Token(string audience, DateTime now, bool notBefore = true) =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Audience = audience,
            NotBefore = notBefore ? now : null,
            Expires = now + TimeSpan.FromDays(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.WebhookSigningKey))
                {
                    KeyId = BrevoWebhookTokens.Audience
                },
                SecurityAlgorithms.HmacSha256)
        });

    // IdentityModel won't sign HS512 with a key under 64 bytes.
    private static string Hs512Token(DateTime now)
    {
        var seconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        var header = Base64UrlEncoder.Encode($$"""{"alg":"HS512","typ":"JWT","kid":"{{BrevoWebhookTokens.Audience}}"}""");
        var payload = Base64UrlEncoder.Encode(
            $$"""{"aud":"{{BrevoWebhookTokens.Audience}}","nbf":{{seconds}},"exp":{{seconds + 86400}}}""");
        var signature = HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(ApiFactory.WebhookSigningKey), Encoding.ASCII.GetBytes($"{header}.{payload}"));
        return $"{header}.{payload}.{Base64UrlEncoder.Encode(signature)}";
    }

    private string WebhookToken() =>
        BrevoWebhookTokens.Mint(ApiFactory.WebhookSigningKey, Time.GetUtcNow().UtcDateTime);

    private Task<HttpResponseMessage> PostEventAsync(object payload) => PostEventAsync(payload, WebhookToken());

    private async Task<HttpResponseMessage> PostEventAsync(object payload, string? bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = payload as HttpContent ?? JsonContent.Create(payload)
        };
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await Https.SendAsync(request);
    }
}
