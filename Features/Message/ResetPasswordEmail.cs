namespace Laneway.Api;

public static class ResetPasswordEmail
{
    public static EmailBody Render(ResetPassword message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        return new EmailBody(
            "Reset your Laneway password",
            [
                new Paragraph($"Someone asked to reset the password for @{message.Username}."),
                new ButtonLink("Reset password", links.To(AppRoutes.ResetPassword, message.Token)),
                new Paragraph(
                    "The link works for 1 hour. If it wasn't you, ignore this email. Your password hasn't changed."),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
