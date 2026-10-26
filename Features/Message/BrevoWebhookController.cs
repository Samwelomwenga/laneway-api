using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route(Path)]
public class BrevoWebhookController(BrevoWebhookTokens tokens, BrevoWebhook webhook, ILoggerFactory loggers)
    : ControllerBase
{
    public const string Path = "api/v1/webhooks/brevo";

    private readonly ILogger _log = loggers.CreateLogger(MessageLog.Category);

    // Brevo discards an event answered with a 5xx and retries only a 429 or a timeout.
    [HttpPost]
    [AllowAnonymous]
    [OwnBearerToken]
    [RateLimited(RateLimits.Webhook)]
    public async Task<ActionResult> Receive()
    {
        if (await tokens.RejectionAsync(Request.Headers.Authorization.ToString()) is { } reason)
        {
            _log.WebhookRejected(reason, ClientIp.Of(HttpContext));
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized();
        }

        JsonDocument payload;
        try
        {
            payload = await JsonDocument.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            _log.WebhookEventIgnored(BrevoWebhook.Other);
            return NoContent();
        }

        using (payload)
        {
            await webhook.ReceiveAsync(payload.RootElement, HttpContext.RequestAborted);
        }

        return NoContent();
    }
}
