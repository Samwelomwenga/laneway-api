namespace Laneway.Api;

public sealed record EmailContent(string To, string Subject, string Html, string Text, string Tag);

public sealed record EmailSent(string ProviderMessageId);

public interface IEmailProvider
{
    Task<EmailSent> SendAsync(EmailContent content, Guid idempotencyKey, CancellationToken token);
}

/// <summary>
/// The provider answered with an error status. <see cref="Code"/> is set only when it is one the provider documents.
/// </summary>
public sealed class EmailProviderException : Exception
{
    public EmailProviderException()
    {
    }

    public EmailProviderException(string message) : base(message)
    {
    }

    public EmailProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public EmailProviderException(int status, string? code) : base($"The email provider answered {status}.")
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }

    public string? Code { get; }
}
