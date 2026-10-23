namespace Laneway.Api;

public sealed record EmailContent(string To, string Subject, string Html, string Text, string Tag);

public sealed record EmailSent(string ProviderMessageId);

public interface IEmailProvider
{
    Task<EmailSent> SendAsync(EmailContent content, Guid idempotencyKey, CancellationToken token);
}
