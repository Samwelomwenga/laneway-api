namespace Laneway.Api;

public static class VerifyEmailEmail
{
    public static EmailBody Render(VerifyEmail message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        return new EmailBody(
            "Confirm your email for Laneway",
            [
                new Paragraph("Confirm this is your email to finish signing up for Laneway."),
                new ButtonLink("Confirm email", links.To(AppRoutes.VerifyEmail, message.Token)),
                new Paragraph(
                    "The link works for 24 hours. If you didn't sign up, ignore this email. The account is removed after 7 days."),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
