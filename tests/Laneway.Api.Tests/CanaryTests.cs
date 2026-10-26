using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class CanaryTests : ApiTests
{
    private const string Password = "canary heron ledger 7f3a";

    private const string ResetPassword = "canary walrus lantern 9c1e";

    private const string UndoPassword = "canary marten quarry 2b8d";

    private const string UserAgent = "CanaryBrowser/7.3 (canary-agent-5d2e)";

    private const string PathSegment = "canary-segment-4e9b";

    private const string QueryValue = "canary-query-81c0";

    private readonly List<string> _planted = [Password, ResetPassword, UndoPassword, UserAgent, PathSegment, QueryValue];

    public CanaryTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task No_planted_secret_reaches_an_exported_log_span_or_metric()
    {
        var email = Plant($"canary.{Guid.NewGuid():n}@example.com");
        var newEmail = Plant($"canary.{Guid.NewGuid():n}@example.com");

        using (var signUp = await SendAsync("/api/v1/auth/sign-up", SignUpBody(email, Password)))
        {
            Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        }

        var verifyToken = Plant((await AwaitEmailAsync(email)).TokenFrom(AppRoutes.VerifyEmail));
        using (var verify = await SendAsync("/api/v1/auth/verify-email", new { token = verifyToken }))
        {
            await SignedInAsync(verify);
        }

        using (var signIn = await SendAsync("/api/v1/auth/sign-in", new { email, password = Password }))
        {
            var cookie = (await SignedInAsync(signIn)).Cookie;
            using var refresh = await SendAsync("/api/v1/auth/refresh", body: null, cookie: cookie);
            await SignedInAsync(refresh);
        }

        using (var forgot = await SendAsync("/api/v1/auth/forgot-password", new { email }))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        var resetToken = Plant((await Brevo.AwaitEmailAsync(email, nameof(MessageType.ResetPassword))).TokenFrom(AppRoutes.ResetPassword));
        using (var reset = await SendAsync("/api/v1/auth/reset-password", new { token = resetToken, newPassword = ResetPassword }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        string accessToken;
        using (var signIn = await SendAsync("/api/v1/auth/sign-in", new { email, password = ResetPassword }))
        {
            accessToken = (await SignedInAsync(signIn)).AccessToken;
        }

        using (var change = await SendAsync(
            "/api/v1/auth/change-email", new { password = ResetPassword, newEmail }, bearer: accessToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        }

        var confirmToken = Plant((await AwaitEmailAsync(newEmail)).TokenFrom(AppRoutes.ConfirmEmailChange));
        using (var confirm = await SendAsync("/api/v1/auth/confirm-email-change", new { token = confirmToken }))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        using (var unmatched = await SendAsync(
            $"/api/v1/{PathSegment}?canary={QueryValue}", body: null, bearer: accessToken, method: HttpMethod.Get))
        {
            Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);
        }

        var notice = await Brevo.AwaitEmailAsync(email, nameof(MessageType.EmailChanged));
        var undoToken = Plant(notice.TokenFrom(AppRoutes.UndoEmailChange));
        using (var undo = await SendAsync(
            "/api/v1/auth/undo-email-change",
            new { token = undoToken, recentPassword = ResetPassword, newPassword = UndoPassword }))
        {
            Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        }

        using (var webhook = await SendAsync(
            "/api/v1/webhooks/brevo",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["event"] = "hard_bounce",
                ["email"] = email,
                ["message-id"] = notice.MessageId
            },
            bearer: BrevoWebhookTokens.Mint(ApiFactory.WebhookSigningKey, Time.GetUtcNow().UtcDateTime)))
        {
            Assert.Equal(HttpStatusCode.NoContent, webhook.StatusCode);
        }

        await AwaitLogsAsync("AddressSuppressed", 1);

        Assert.Contains(ShippedSpans, span => span.GetTagItem("server.address") as string == "api.pwnedpasswords.com");
        Assert.Contains(ShippedLogs, log => log.EventId.Name == "DeliverySent");
        var exported = Exported();
        Assert.All(_planted, planted => Assert.DoesNotContain(
            exported, text => text.Contains(planted, StringComparison.OrdinalIgnoreCase)));
    }

    private string Plant(string secret)
    {
        _planted.Add(secret);
        return secret;
    }

    private async Task<(string AccessToken, string Cookie)> SignedInAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accessToken = Plant((await response.ReadEnvelope<SignedInDto>()).Data!.AccessToken);
        Plant(accessToken.Split('.')[2]);
        var cookie = Plant(RefreshCookieAssert.Set(response, Session.AbsoluteLifetime));
        return (accessToken, cookie);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string path, object? body, string? bearer = null, string? cookie = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        foreach (var (name, value) in BrowserHeaders())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieAssert.Name}={cookie}");
        }

        return await Https.SendAsync(request);
    }

    private List<string> Exported()
    {
        var texts = new List<string>();
        foreach (var log in ShippedLogs)
        {
            texts.AddRange([log.CategoryName ?? string.Empty, log.Body ?? string.Empty, log.FormattedMessage ?? string.Empty]);
            texts.AddRange((log.Attributes ?? []).Select(pair => $"{pair.Key}={pair.Value}"));
            texts.Add(log.Exception?.ToString() ?? string.Empty);
            log.ForEachScope(
                (scope, _) =>
                {
                    foreach (var pair in scope)
                    {
                        texts.Add($"{pair.Key}={pair.Value}");
                    }
                },
                (object?)null);
        }

        foreach (var span in ShippedSpans)
        {
            texts.AddRange([span.DisplayName, span.OperationName, span.StatusDescription ?? string.Empty]);
            texts.AddRange(span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
            texts.AddRange(span.Baggage.Select(item => $"{item.Key}={item.Value}"));
            texts.AddRange(span.Events.SelectMany(recorded =>
                recorded.Tags.Select(tag => $"{tag.Key}={tag.Value}").Append(recorded.Name)));
            texts.AddRange(span.Links.SelectMany(link =>
                (link.Tags ?? []).Select(tag => $"{tag.Key}={tag.Value}")));
        }

        foreach (var metric in ShippedMetrics())
        {
            texts.Add(metric.Name);
            foreach (var point in metric.MetricPoints)
            {
                foreach (var tag in point.Tags)
                {
                    texts.Add($"{tag.Key}={tag.Value}");
                }
            }
        }

        return texts;
    }
}
