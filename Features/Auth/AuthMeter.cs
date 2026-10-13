using System.Diagnostics.Metrics;

namespace Laneway.Api;

public sealed class AuthMeter
{
    public const string Name = "Laneway.Auth";

    public const string SignInAttemptsName = "laneway.auth.sign_in.attempts";

    public const string SignInBlocksName = "laneway.auth.sign_in.blocks";

    public const string PasswordCheckFailuresName = "laneway.auth.password_check.failures";

    public const string BreachedCheckUnavailableName = "laneway.auth.breached_check.unavailable";

    public const string RefreshName = "laneway.auth.refresh";

    public const string CsrfRejectionsName = "laneway.auth.csrf.rejections";

    public const string RateLimitRejectionsName = "laneway.auth.rate_limit.rejections";

    private readonly Counter<long> _signInAttempts;

    private readonly Counter<long> _signInBlocks;

    private readonly Counter<long> _passwordCheckFailures;

    private readonly Counter<long> _breachedCheckUnavailable;

    private readonly Counter<long> _refresh;

    private readonly Counter<long> _csrfRejections;

    private readonly Counter<long> _rateLimitRejections;

    public AuthMeter(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Meter = factory.Create(Name);
        _signInAttempts = Meter.CreateCounter<long>(SignInAttemptsName);
        _signInBlocks = Meter.CreateCounter<long>(SignInBlocksName);
        _passwordCheckFailures = Meter.CreateCounter<long>(PasswordCheckFailuresName);
        _breachedCheckUnavailable = Meter.CreateCounter<long>(BreachedCheckUnavailableName);
        _refresh = Meter.CreateCounter<long>(RefreshName);
        _csrfRejections = Meter.CreateCounter<long>(CsrfRejectionsName);
        _rateLimitRejections = Meter.CreateCounter<long>(RateLimitRejectionsName);
    }

    public Meter Meter { get; }

    public void SignInAttempted(SignInResult result) =>
        _signInAttempts.Add(1, new KeyValuePair<string, object?>("result", result switch
        {
            SignInResult.Succeeded => "succeeded",
            SignInResult.WrongPassword => "wrong_password",
            SignInResult.UnknownAccount => "unknown_account",
            SignInResult.Blocked => "blocked",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        }));

    public void SignInBlockStarted() => _signInBlocks.Add(1);

    public void PasswordCheckFailed(PasswordCheckedEndpoint endpoint, bool blocked) =>
        _passwordCheckFailures.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", endpoint switch
            {
                PasswordCheckedEndpoint.ChangePassword => "change_password",
                PasswordCheckedEndpoint.ChangeEmail => "change_email",
                PasswordCheckedEndpoint.DeleteUser => "delete_user",
                _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null)
            }),
            new KeyValuePair<string, object?>("blocked", blocked));

    public void BreachedCheckUnavailable() => _breachedCheckUnavailable.Add(1);

    public void Refreshed(RefreshResult result) =>
        _refresh.Add(1, new KeyValuePair<string, object?>("result", result switch
        {
            RefreshResult.Rotated => "rotated",
            RefreshResult.Superseded => "superseded",
            RefreshResult.Reused => "reused",
            RefreshResult.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        }));

    public void CsrfRejected(FailedCsrfCheck check) =>
        _csrfRejections.Add(1, new KeyValuePair<string, object?>("check", check switch
        {
            FailedCsrfCheck.Header => "header",
            FailedCsrfCheck.Origin => "origin",
            FailedCsrfCheck.FetchSite => "fetch_site",
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, null)
        }));

    public void RateLimited(string policy) =>
        _rateLimitRejections.Add(1, new KeyValuePair<string, object?>("policy", policy));
}

public enum SignInResult
{
    Succeeded,
    WrongPassword,
    UnknownAccount,
    Blocked
}

public enum PasswordCheckedEndpoint
{
    ChangePassword,
    ChangeEmail,
    DeleteUser
}

public enum RefreshResult
{
    Rotated,
    Superseded,
    Reused,
    Rejected
}
