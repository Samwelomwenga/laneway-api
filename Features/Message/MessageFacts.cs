using System.Globalization;
using MyCSharp.HttpUserAgentParser;
using MyCSharp.HttpUserAgentParser.Providers;
using TimeZoneNames;

namespace Laneway.Api;

public static class MessageFacts
{
    private static readonly HttpUserAgentParserCachedProvider UserAgents = new();

    /// <summary>Formats a UTC time in the named zone, as "20 October 2026 at 14:05 East Africa Time".</summary>
    public static string When(DateTime utc, string timeZone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var name = TZNames.GetNamesForTimeZone(zone.Id, "en").Generic ?? zone.StandardName;

        return $"{local.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)} {name}";
    }

    /// <summary>Masks an address as "j•••@g•••.com". A one-character local part shows as "•••".</summary>
    public static string Mask(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var at = address.LastIndexOf('@');
        var (local, domain) = (address[..at], address[(at + 1)..]);
        var label = domain.IndexOf('.', StringComparison.Ordinal) is var dot and >= 0 ? dot : domain.Length;
        var maskedLocal = local.Length == 1 ? "•••" : $"{local[0]}•••";

        return $"{maskedLocal}@{domain[0]}•••{domain[label..]}";
    }

    /// <summary>Names the browser and the system, as ("Firefox", "Linux"), or the unknown wording for each.</summary>
    public static (string Browser, string System) Browser(string userAgent)
    {
        var parsed = UserAgents.Parse(userAgent);
        var browser = parsed.Type == HttpUserAgentType.Browser && !string.IsNullOrEmpty(parsed.Name)
            ? parsed.Name
            : "an unknown browser";
        var system = parsed.Platform?.PlatformType switch
        {
            HttpUserAgentPlatformType.Windows => "Windows",
            HttpUserAgentPlatformType.Linux => "Linux",
            HttpUserAgentPlatformType.Unix => "Unix",
            HttpUserAgentPlatformType.IOS => "iOS",
            HttpUserAgentPlatformType.MacOS => "macOS",
            HttpUserAgentPlatformType.BlackBerry => "BlackBerry",
            HttpUserAgentPlatformType.Android => "Android",
            HttpUserAgentPlatformType.Symbian => "Symbian",
            HttpUserAgentPlatformType.ChromeOS => "ChromeOS",
            _ => "an unknown system"
        };

        return (browser, system);
    }
}
