namespace Laneway.Api;

public sealed class PasswordPolicy(BreachedPasswords breached, AuthMeter meter, ILoggerFactory loggers)
{
    public const int ContextWordMinimumLength = 4;

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public async Task<ApiError?> CheckAsync(string password, string username, string email, string field = "password")
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(email);

        if (CommonPasswords.Contains(password))
        {
            return new ApiError(
                field, ErrorCodes.CommonPassword, $"'{field}' is on a list of common passwords.");
        }

        if (ContextWords(username, email).Any(word => password.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return new ApiError(
                field,
                ErrorCodes.ContainsContextWord,
                $"'{field}' can't contain your username, the part of your email before the @, or 'laneway'.");
        }

        if (await IsBreachedAsync(password))
        {
            return new ApiError(
                field, ErrorCodes.BreachedPassword, $"'{field}' appears in a known data breach.");
        }

        return null;
    }

    private static IEnumerable<string> ContextWords(string username, string email)
    {
        var at = email.LastIndexOf('@');
        return new[] { "laneway", username, at < 0 ? email : email[..at] }
            .Select(word => Passwords.Normalize(word))
            .Where(word => Passwords.Length(word) >= ContextWordMinimumLength);
    }

    private async Task<bool> IsBreachedAsync(string password)
    {
        try
        {
            return await breached.ContainsAsync(password);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _log.BreachedCheckUnavailable(exception);
            meter.BreachedCheckUnavailable();
            return false;
        }
    }
}
