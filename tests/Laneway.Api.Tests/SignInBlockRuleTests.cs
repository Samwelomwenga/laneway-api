using Xunit;

namespace Laneway.Api.Tests;

public class SignInBlockRuleTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 2)]
    [InlineData(70, 14)]
    [InlineData(75, 15)]
    [InlineData(80, 15)]
    public void Every_5th_failure_blocks_for_a_minute_per_5_failures_up_to_15(int failures, int minutes)
    {
        var user = UserWithFailures(failures - 1);

        var started = SignInBlock.RecordFailure(user, Start);

        Assert.True(started);
        Assert.Equal(failures, user.FailedSignInCount);
        Assert.Equal(Start, user.LastFailedSignInAt);
        Assert.Equal(Start.AddMinutes(minutes), user.SignInBlockedUntil);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(9)]
    public void A_failure_that_is_not_a_multiple_of_5_starts_no_block(int failures)
    {
        var user = UserWithFailures(failures - 1);

        Assert.False(SignInBlock.RecordFailure(user, Start));
        Assert.Null(user.SignInBlockedUntil);
    }

    [Fact]
    public void The_count_starts_over_after_12_hours_with_no_failure()
    {
        var user = UserWithFailures(4);

        var started = SignInBlock.RecordFailure(user, Start.AddHours(12));

        Assert.False(started);
        Assert.Equal(1, user.FailedSignInCount);
    }

    [Fact]
    public void A_failure_just_inside_12_hours_keeps_counting()
    {
        var user = UserWithFailures(4);

        Assert.True(SignInBlock.RecordFailure(user, Start.AddHours(12).AddSeconds(-1)));
        Assert.Equal(5, user.FailedSignInCount);
    }

    [Fact]
    public void A_user_is_blocked_until_the_block_ends()
    {
        var user = UserWithFailures(4);
        SignInBlock.RecordFailure(user, Start);

        Assert.True(SignInBlock.IsBlocked(user, Start.AddSeconds(59)));
        Assert.False(SignInBlock.IsBlocked(user, Start.AddMinutes(1)));
    }

    private static User UserWithFailures(int count) => new()
    {
        FirstName = "Ana",
        LastName = "Waweru",
        Username = "ana",
        Email = "ana@example.com",
        PasswordHash = "unused",
        Language = "en",
        TimeZone = "UTC",
        Location = "Nairobi",
        FailedSignInCount = count,
        LastFailedSignInAt = count > 0 ? Start : null
    };
}
