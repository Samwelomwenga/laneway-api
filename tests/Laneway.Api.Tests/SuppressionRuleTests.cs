using Xunit;

namespace Laneway.Api.Tests;

public class SuppressionRuleTests
{
    private static readonly MessageRoute Notice =
        new([MessageChannel.Email], EmailByNature: false, OptOut: false, TimeSpan.FromHours(24));

    private static readonly MessageRoute OptOut =
        new([MessageChannel.Email], EmailByNature: true, OptOut: true, TimeSpan.FromHours(24));

    [Theory]
    [InlineData(SuppressionKind.HardBounce)]
    [InlineData(SuppressionKind.Spam)]
    [InlineData(SuppressionKind.Blocked)]
    public void A_bounce_a_complaint_or_a_block_stops_every_message(SuppressionKind kind)
    {
        Assert.True(Suppressions.Stops(kind, Notice));
        Assert.True(Suppressions.Stops(kind, OptOut));
    }

    [Fact]
    public void An_unsubscribe_stops_only_opt_out_messages()
    {
        Assert.False(Suppressions.Stops(SuppressionKind.Unsubscribed, Notice));
        Assert.True(Suppressions.Stops(SuppressionKind.Unsubscribed, OptOut));
    }
}
