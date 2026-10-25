namespace Laneway.Api;

public static class ConfirmEmailChangeEmail
{
    public static EmailBody Render(ConfirmEmailChange message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        return new EmailBody(
            "Confirm your new Laneway email",
            [
                new Paragraph($"The Laneway account @{message.Username} asked to use this address for signing in."),
                new ButtonLink("Confirm new email", links.To(AppRoutes.ConfirmEmailChange, message.Token)),
                new Paragraph(
                    "The link works for 24 hours. If you didn't ask for this, ignore this email and nothing changes."),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
