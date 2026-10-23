namespace Laneway.Api;

public static class PasswordChangedEmail
{
    public static EmailBody Render(PasswordChanged message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        var (browser, system) = MessageFacts.Browser(message.UserAgent);
        return new EmailBody(
            "Your Laneway password was changed",
            [
                new Paragraph(
                    $"Your password was changed on {MessageFacts.When(message.At, message.TimeZone)} from {browser} on {system}. We signed you out everywhere else."),
                new Paragraph("If this wasn't you, reset your password now:"),
                new ButtonLink("Reset your password", links.To(AppRoutes.ForgotPassword)),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
