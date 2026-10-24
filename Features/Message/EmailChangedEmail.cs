namespace Laneway.Api;

public static class EmailChangedEmail
{
    public static EmailBody Render(EmailChanged message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        var (browser, system) = MessageFacts.Browser(message.UserAgent ?? string.Empty);
        return new EmailBody(
            "Your Laneway email was changed",
            [
                new Paragraph(
                    $"Your Laneway email was changed to {MessageFacts.Mask(message.NewEmail)} on "
                    + $"{MessageFacts.When(message.At, message.TimeZone)} from {browser} on {system}. "
                    + "We'll no longer send mail here."),
                new Paragraph("If this wasn't you, undo the change within 7 days:"),
                new ButtonLink("Undo the change", links.To(AppRoutes.UndoEmailChange, message.Token)),
                new Paragraph(
                    "You'll need a password you used on Laneway in the last week, and you'll choose a new one. "
                    + "Undoing it signs everyone out."),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
