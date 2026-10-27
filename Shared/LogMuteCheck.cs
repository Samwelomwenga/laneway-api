using Serilog.Events;

namespace Laneway.Api;

public static class LogMuteCheck
{
    private const string Root = "Laneway.";

    public static void ThrowIfLanewayMuted(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var serilog = configuration.GetSection("Serilog:MinimumLevel");
        var serilogDefault = serilog.Value is null ? serilog.GetSection("Default") : serilog;
        var problems = Problems(serilogDefault, serilog.GetSection("Override"))
            .Concat(Problems(
                configuration.GetSection("Logging:OpenTelemetry:LogLevel:Default"),
                configuration.GetSection("Logging:OpenTelemetry:LogLevel")))
            .ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", problems)} would drop Information events from Laneway.* categories, "
                + "which must log in every environment. Set it to Information or Debug.");
        }
    }

    private static IEnumerable<string> Problems(IConfigurationSection fallback, IConfigurationSection rules)
    {
        var levels = rules.GetChildren()
            .Where(rule => rule.Value is not null && rule.Key != "Default")
            .ToList();

        var covering = levels
            .Where(rule => !rule.Key.Contains('*', StringComparison.Ordinal)
                && Root.StartsWith(rule.Key, StringComparison.OrdinalIgnoreCase))
            .MaxBy(rule => rule.Key.Length) ?? (fallback.Value is null ? null : fallback);
        if (covering is not null && Mutes(covering.Value!))
        {
            yield return $"{covering.Path} = {covering.Value}";
        }

        foreach (var rule in levels.Where(rule => rule != covering && Reaches(rule.Key) && Mutes(rule.Value!)))
        {
            yield return $"{rule.Path} = {rule.Value}";
        }
    }

    // Microsoft.Extensions.Logging matches a rule by category prefix, and a * in it stands for any run of characters.
    private static bool Reaches(string key)
    {
        var prefix = key.Split('*')[0];
        return Root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith(Root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Mutes(string level) =>
        (Enum.TryParse<LogLevel>(level, ignoreCase: true, out var dotnet) && dotnet > LogLevel.Information)
        || (Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var serilog) && serilog > LogEventLevel.Information);
}
