using System.Globalization;
using Xunit;

namespace Laneway.Api.Tests;

public static class RefreshCookieAssert
{
    public const string Name = "__Host-Http-lnw_refresh";

    public static string Set(HttpResponseMessage response, TimeSpan maxAge)
    {
        var attributes = Attributes(response);
        var value = attributes[Name];
        Assert.Matches("^lnw_rt_[A-Za-z0-9_-]{43}$", value);
        var seconds = (long)maxAge.TotalSeconds;
        Assert.InRange(long.Parse(attributes["max-age"], CultureInfo.InvariantCulture), seconds - 1, seconds);
        return value;
    }

    public static void Cleared(HttpResponseMessage response)
    {
        var attributes = Attributes(response);
        Assert.Equal(string.Empty, attributes[Name]);
        Assert.Equal("0", attributes["max-age"]);
    }

    public static void Untouched(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private static Dictionary<string, string> Attributes(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var attributes = header.Split(';', StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => pair[0],
                pair => pair.Length == 2 ? pair[1] : string.Empty,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(Name, attributes.Keys.First());
        Assert.Contains("secure", attributes.Keys);
        Assert.Contains("httponly", attributes.Keys);
        Assert.Equal("strict", attributes["samesite"], ignoreCase: true);
        Assert.Equal("/", attributes["path"]);
        Assert.DoesNotContain("domain", attributes.Keys);
        Assert.DoesNotContain("expires", attributes.Keys);
        return attributes;
    }
}
