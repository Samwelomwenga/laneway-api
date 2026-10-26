using Xunit;

namespace Laneway.Api.Tests;

public class DatabaseStartupTests
{
    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("include error detail=True")]
    [InlineData("IncludeErrorDetail=true")]
    public void A_connection_string_with_postgres_error_detail_stops_the_app_at_startup(string setting)
    {
        using var factory = new ApiFactory($"Host=localhost;Database=never_opened;{setting}");

        var thrown = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Include Error Detail", thrown.Message, StringComparison.Ordinal);
    }
}
