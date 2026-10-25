namespace Laneway.Api;

public static class EmailChangeUndoneEmail
{
    public static EmailBody Render(EmailChangeUndone message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new EmailBody(
            "Your Laneway email change was undone",
            [
                new Paragraph(
                    "The change of your Laneway email to this address was undone on "
                    + $"{MessageFacts.When(message.At, message.TimeZone)}, and your password was changed."),
                new Paragraph(
                    "Whoever did it could read your old email and knew a password you used on Laneway recently. "
                    + "If this wasn't you, stop using that password anywhere else."),
                new Footnote(EmailLayout.AccountFooter)
            ]);
    }
}
