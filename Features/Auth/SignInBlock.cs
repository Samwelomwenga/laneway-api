namespace Laneway.Api;

public static class SignInBlock
{
    public const int FailuresPerStep = 5;

    public static readonly TimeSpan LengthPerStep = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan LongestBlock = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan CountResetsAfter = TimeSpan.FromHours(12);

    public static bool IsBlocked(User user, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.SignInBlockedUntil > now;
    }

    public static bool RecordFailure(User user, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.LastFailedSignInAt is not { } last || now - last >= CountResetsAfter)
        {
            user.FailedSignInCount = 0;
        }

        user.FailedSignInCount++;
        user.LastFailedSignInAt = now;
        if (user.FailedSignInCount % FailuresPerStep != 0)
        {
            return false;
        }

        var length = LengthPerStep * (user.FailedSignInCount / FailuresPerStep);
        user.SignInBlockedUntil = now + (length < LongestBlock ? length : LongestBlock);
        return true;
    }
}
