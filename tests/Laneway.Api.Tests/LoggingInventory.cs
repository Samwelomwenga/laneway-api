using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Laneway.Api.Tests;

internal sealed record ShippedLog(string Category, LogLevel Level, string? EventName, IReadOnlyCollection<string> Keys);

internal sealed record ShippedCounter(string MeterName, string Name, IReadOnlyCollection<string> TagKeys);

internal sealed partial class LoggingInventory
{
    private static readonly string[] FrameworkPrefixes = ["Microsoft.", "System."];

    private readonly Dictionary<(string Category, string Event), HashSet<string>> _events = [];

    private readonly Dictionary<string, HashSet<string>> _counters = new(StringComparer.Ordinal);

    private readonly HashSet<string> _everyEntry = new(StringComparer.Ordinal);

    private LoggingInventory()
    {
    }

    public static LoggingInventory Documented { get; } = Parse(ReadMarkdown());

    public static string ReadMarkdown([CallerFilePath] string source = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "../../docs/security/logging-inventory.md"));

    public static LoggingInventory Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var inventory = new LoggingInventory();
        foreach (var section in Sections(markdown))
        {
            inventory.Read(section);
        }

        return inventory;
    }

    public List<string> Violations(IEnumerable<ShippedLog> logs, IEnumerable<ShippedCounter> counters)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(counters);

        var violations = new List<string>();
        foreach (var log in logs)
        {
            if (log.Level >= LogLevel.Warning && IsFramework(log.Category))
            {
                continue;
            }

            if (log.EventName is null || !_events.TryGetValue((log.Category, log.EventName), out var fields))
            {
                violations.Add($"{log.Category} {log.EventName ?? "(no event name)"} has no row in the inventory.");
                continue;
            }

            violations.AddRange(log.Keys
                .Where(key => key != "{OriginalFormat}" && !_everyEntry.Contains(key) && !fields.Contains(key))
                .Select(key => $"{log.Category} {log.EventName} ships `{key}`, which its row doesn't list."));
        }

        foreach (var counter in counters.Where(counter => counter.MeterName.StartsWith("Laneway.", StringComparison.Ordinal)))
        {
            if (!_counters.TryGetValue(counter.Name, out var tags))
            {
                violations.Add($"{counter.Name} on {counter.MeterName} has no row in a counter table.");
                continue;
            }

            violations.AddRange(counter.TagKeys
                .Where(key => !tags.Contains(key))
                .Select(key => $"{counter.Name} ships tag `{key}`, which its row doesn't list."));
        }

        return [.. violations.Distinct(StringComparer.Ordinal)];
    }

    private static bool IsFramework(string category) =>
        FrameworkPrefixes.Any(prefix => category.StartsWith(prefix, StringComparison.Ordinal));

    private sealed record Section(string Name, List<string> Lines);

    private static IEnumerable<Section> Sections(string markdown)
    {
        Section? current = null;
        foreach (var line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = new Section(line[3..].Trim(), []);
            }
            else
            {
                current?.Lines.Add(line);
            }
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    private void Read(Section section)
    {
        var prose = string.Join('\n', section.Lines.Where(line => !line.StartsWith('|')));
        var category = CategoryLine().Match(prose) is { Success: true } named ? named.Groups[1].Value : null;
        var scope = ScopeLine().Match(prose) is { Success: true } carried
            ? Backticked(carried.Groups[1].Value).ToList()
            : [];

        foreach (var table in Tables(section.Lines))
        {
            var header = string.Join(" | ", table[0]);
            switch (header)
            {
                case "Id | Event | Level | Fields":
                    if (category is null)
                    {
                        throw Reshaped(section, "has an event table but no \"under the `X` category\" line");
                    }

                    foreach (var row in Rows(section, table))
                    {
                        AddEvent(section, category, row[1], row[3], scope);
                    }

                    break;
                case "Category | Id | Event | Level | Fields":
                    foreach (var row in Rows(section, table))
                    {
                        AddEvent(section, SingleName(section, row[0]), row[2], row[4], scope);
                    }

                    break;
                case "Counter | Tags":
                    foreach (var row in Rows(section, table))
                    {
                        _counters[SingleName(section, row[0])] = TagKeys().Matches(row[1])
                            .Select(match => match.Groups[1].Value)
                            .ToHashSet(StringComparer.Ordinal);
                    }

                    break;
                case "Rule | Source | Tier | Playbook":
                    break;
                case "Field | Carries":
                    foreach (var row in Rows(section, table))
                    {
                        _everyEntry.Add(SingleName(section, row[0]));
                    }

                    break;
                default:
                    throw Reshaped(section, $"has a table the allowlist test can't read: | {header} |");
            }
        }
    }

    private void AddEvent(Section section, string category, string eventCell, string fieldsCell, List<string> scope)
    {
        var key = (category, SingleName(section, eventCell));
        if (!_events.TryGetValue(key, out var fields))
        {
            _events[key] = fields = new HashSet<string>(scope, StringComparer.Ordinal);
        }

        fields.UnionWith(Backticked(fieldsCell));
    }

    private static IEnumerable<List<string[]>> Tables(List<string> lines)
    {
        var table = new List<string[]>();
        foreach (var line in lines.Append(string.Empty))
        {
            if (line.StartsWith('|'))
            {
                table.Add([.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())]);
            }
            else if (table.Count > 0)
            {
                yield return table;
                table = [];
            }
        }
    }

    private static IEnumerable<string[]> Rows(Section section, List<string[]> table)
    {
        foreach (var row in table.Skip(2))
        {
            if (row.Length != table[0].Length)
            {
                throw Reshaped(section, $"has a row with {row.Length} cells under a {table[0].Length}-column header");
            }

            yield return row;
        }
    }

    private static string SingleName(Section section, string cell)
    {
        var names = Backticked(cell).ToList();
        return names.Count == 1 ? names[0] : throw Reshaped(section, $"has a cell that should name one thing: {cell}");
    }

    private static IEnumerable<string> Backticked(string text) =>
        CodeSpan().Matches(text).Select(match => match.Groups[1].Value.Split(':')[0].Trim());

    private static InvalidDataException Reshaped(Section section, string problem) =>
        new($"The logging inventory's \"{section.Name}\" section {problem}.");

    [GeneratedRegex("under the `([^`]+)` category")]
    private static partial Regex CategoryLine();

    [GeneratedRegex("(?:scope that|also) carries ([^.]+)\\.")]
    private static partial Regex ScopeLine();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex("`([^`]+)`:")]
    private static partial Regex TagKeys();
}
