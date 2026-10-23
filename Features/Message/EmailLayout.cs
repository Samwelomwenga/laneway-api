using System.Text;
using System.Text.Encodings.Web;

namespace Laneway.Api;

public abstract record EmailBlock;

public sealed record Paragraph(string Text) : EmailBlock;

public sealed record ButtonLink(string Label, Uri Url) : EmailBlock;

public sealed record Footnote(string Text) : EmailBlock;

public sealed record EmailBody(string Subject, IReadOnlyList<EmailBlock> Blocks);

public static class EmailLayout
{
    public const string AccountFooter = "You got this email because it belongs to a Laneway account.";

    private const string Muted = "margin:0 0 16px;font-size:13px;color:#57606a;";

    public static string Html(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width\">\n")
            .Append("<title>").Append(Encode(body.Subject)).Append("</title>\n</head>\n")
            .Append("<body style=\"margin:0;padding:0;background:#f6f8fa;\">\n")
            .Append("<div style=\"max-width:560px;margin:0 auto;padding:32px 24px;background:#ffffff;")
            .Append("font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#1f2328;\">\n")
            .Append("<p style=\"margin:0 0 24px;font-size:20px;font-weight:600;\">Laneway</p>\n");
        foreach (var block in body.Blocks)
        {
            html.Append(block switch
            {
                Paragraph paragraph => $"<p style=\"margin:0 0 16px;\">{Encode(paragraph.Text)}</p>\n",
                ButtonLink button =>
                    $"<p style=\"margin:24px 0 8px;\"><a href=\"{Encode(button.Url.AbsoluteUri)}\" style=\"display:inline-block;padding:10px 18px;background:#1f6feb;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600;\">{Encode(button.Label)}</a></p>\n"
                    + $"<p style=\"{Muted}word-break:break-all;\">{Encode(button.Url.AbsoluteUri)}</p>\n",
                Footnote footnote => $"<p style=\"margin:32px 0 0;font-size:13px;color:#57606a;\">{Encode(footnote.Text)}</p>\n",
                _ => throw new ArgumentOutOfRangeException(nameof(body), block, null)
            });
        }

        return html.Append("</div>\n</body>\n</html>\n").ToString();
    }

    public static string Text(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var parts = body.Blocks.Select(block => block switch
        {
            Paragraph paragraph => paragraph.Text,
            ButtonLink button => button.Url.AbsoluteUri,
            Footnote footnote => footnote.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(body), block, null)
        });

        return string.Join("\n\n", parts.Prepend("Laneway")) + "\n";
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
