using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

public class EmailSampleTests
{
    private const string RewriteSamplesVariable = "UPDATE_EMAIL_SAMPLES";

    private static readonly AppLinks Links =
        new(Options.Create(new WebAppOptions { BaseUrl = "https://app.laneway.example" }));

    private static readonly DateTime At = new(2026, 10, 20, 11, 5, 0, DateTimeKind.Utc);

    public static TheoryData<string> Samples => [.. Rendered().Keys];

    [Fact]
    public void Every_type_has_a_sample()
    {
        var sampled = Rendered().Values.Select(sample => sample.Type).ToHashSet();

        Assert.All(Enum.GetValues<MessageType>(), type => Assert.Contains(type, sampled));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_rendered_email_matches_its_checked_in_sample(string name)
    {
        var body = Rendered()[name].Body;

        AssertSample($"{name}.html", EmailLayout.Html(body));
        AssertSample($"{name}.txt", EmailLayout.Text(body));
    }

    private static Dictionary<string, (MessageType Type, EmailBody Body)> Rendered() => new(StringComparer.Ordinal)
    {
        ["PasswordChanged-Changed"] = Render(new PasswordChanged(
            Guid.Empty,
            PasswordChangeHow.Changed,
            At,
            "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0",
            "Africa/Nairobi"))
    };

    private static (MessageType, EmailBody) Render(IMessage message) =>
        (message.Type, EmailTemplates.Render(message.Type, MessageBodies.Of(message), Links));

    private static void AssertSample(string file, string rendered)
    {
        var path = Path.Combine(SampleFolder(), file);
        if (Environment.GetEnvironmentVariable(RewriteSamplesVariable) == "1")
        {
            File.WriteAllText(path, rendered);
            return;
        }

        Assert.True(File.Exists(path), $"{file} is missing. Run with {RewriteSamplesVariable}=1 to write it.");
        Assert.Equal(File.ReadAllText(path), rendered);
    }

    private static string SampleFolder([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "EmailSamples");
}
