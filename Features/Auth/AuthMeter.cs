using System.Diagnostics.Metrics;

namespace Laneway.Api;

public sealed class AuthMeter
{
    public const string Name = "Laneway.Auth";

    public const string SignInAttemptsName = "laneway.auth.sign_in.attempts";

    public const string BreachedCheckUnavailableName = "laneway.auth.breached_check.unavailable";

    private readonly Counter<long> _signInAttempts;

    private readonly Counter<long> _breachedCheckUnavailable;

    public AuthMeter(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Meter = factory.Create(Name);
        _signInAttempts = Meter.CreateCounter<long>(SignInAttemptsName);
        _breachedCheckUnavailable = Meter.CreateCounter<long>(BreachedCheckUnavailableName);
    }

    public Meter Meter { get; }

    public void SignInAttempted(SignInResult result) =>
        _signInAttempts.Add(1, new KeyValuePair<string, object?>("result", result switch
        {
            SignInResult.Succeeded => "succeeded",
            SignInResult.WrongPassword => "wrong_password",
            SignInResult.UnknownAccount => "unknown_account",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        }));

    public void BreachedCheckUnavailable() => _breachedCheckUnavailable.Add(1);
}

public enum SignInResult
{
    Succeeded,
    WrongPassword,
    UnknownAccount
}
