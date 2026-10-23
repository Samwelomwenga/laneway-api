using Xunit;

namespace Laneway.Api.Tests;

public class MessageFactsTests
{
    private static readonly DateTime At = new(2026, 10, 20, 11, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void A_time_shows_in_the_users_zone_with_its_generic_name() =>
        Assert.Equal("20 October 2026 at 14:05 East Africa Time", MessageFacts.When(At, "Africa/Nairobi"));

    [Fact]
    public void A_zone_that_does_not_resolve_falls_back_to_utc() =>
        Assert.Equal("20 October 2026 at 11:05 Coordinated Universal Time", MessageFacts.When(At, "Mars/Olympus"));

    [Fact]
    public void A_browser_shows_its_name_and_system() =>
        Assert.Equal(
            ("Firefox", "Linux"),
            MessageFacts.Browser("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0"));

    [Theory]
    [InlineData("")]
    [InlineData("Laneway.Api.Tests")]
    public void An_unparsed_user_agent_reads_as_unknown(string userAgent) =>
        Assert.Equal(("an unknown browser", "an unknown system"), MessageFacts.Browser(userAgent));
}
