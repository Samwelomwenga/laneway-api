using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Laneway.Api.Tests;

public partial class AlertPlaybookTests
{
    private const string Playbooks = "https://github.com/Samwelomwenga/laneway-api/blob/main/docs/runbooks/alerts";

    private static readonly string[] Headings =
    [
        "## What fired",
        "## The first query to run",
        "## Telling noise from attack",
        "## What to do",
        "## When to escalate"
    ];

    [Fact]
    public void Every_rule_links_a_playbook_that_exists()
    {
        var rules = Read("terraform/alerting/rules.tf");
        var titles = TitlePattern().Matches(rules).Count;
        var playbooks = PlaybookPattern().Matches(rules).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(playbooks);
        Assert.Equal(titles, playbooks.Count);
        Assert.Contains($"playbooks = \"{Playbooks}\"", rules, StringComparison.Ordinal);
        Assert.Contains("runbook_url = \"${local.playbooks}/${rule.value.playbook}.md\"", rules, StringComparison.Ordinal);
        Assert.All(playbooks, playbook => Assert.True(
            File.Exists(PathOf($"docs/runbooks/alerts/{playbook}.md")), $"No playbook at docs/runbooks/alerts/{playbook}.md"));
    }

    [Fact]
    public void Every_playbook_has_the_five_headings_and_calls_its_threshold_a_guess()
    {
        var playbooks = Directory.GetFiles(PathOf("docs/runbooks/alerts"), "*.md");

        Assert.NotEmpty(playbooks);
        Assert.All(playbooks, path =>
        {
            var text = File.ReadAllText(path);
            var found = text.Split('\n').Select(line => line.TrimEnd()).Where(line => line.StartsWith("## ", StringComparison.Ordinal));
            Assert.Equal(Headings, found);
            Assert.Contains("starting guess", text, StringComparison.Ordinal);
        });
    }

    private static string Read(string relative) => File.ReadAllText(PathOf(relative));

    private static string PathOf(string relative, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "../..", relative);

    [GeneratedRegex("""^\s*title\s*=\s*"[^"]+"\s*$""", RegexOptions.Multiline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex("""^\s*playbook\s*=\s*"([a-z0-9-]+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PlaybookPattern();
}
