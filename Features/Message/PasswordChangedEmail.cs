namespace Laneway.Api;

public static class PasswordChangedEmail
{
    public static EmailBody Render(PasswordChanged message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        var when = MessageFacts.When(message.At, message.TimeZone);
        if (message.How == PasswordChangeHow.Reset)
        {
            return new EmailBody(
                "Your Laneway password was reset",
                [
                    new Paragraph(
                        $"Your password was reset on {when} using a link sent to this email. We signed you out everywhere."),
                    new Paragraph(
                        "If this wasn't you, someone can read your email. Secure it, then reset your password:"),
                    new ButtonLink("Reset your password", links.To(AppRoutes.ForgotPassword)),
                    new Footnote(EmailLayout.AccountFooter)
                ]);
        }

        var (browser, system) = MessageFacts.Browser(message.UserAgent ?? string.Empty);
        return new EmailBody(
            "Your Laneway password was changed",
            [
                new Paragraph(
                    $"Your password was changed on {when} from {browser} on {system}. We signed you out everywhere else."),
                new Paragraph("If this wasn't you, reset your password now:"),
                new ButtonLink("Reset your password", links.To(AppRoutes.ForgotPassword)),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
