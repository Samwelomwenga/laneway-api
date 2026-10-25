namespace Laneway.Api;

public static class InviteSentEmail
{
    public static EmailBody Render(InviteSent message, AppLinks links)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(links);

        var place = message.Place == InvitePlace.Board ? "board" : "workspace";
        var username = MessageFacts.Clip(message.InviterUsername);
        return new EmailBody(
            $"You're invited to a Laneway {place}",
            [
                new Paragraph(
                    $"{MessageFacts.Clip(message.InviterName)} (@{username}) invited you to the {place} "
                    + $"\"{MessageFacts.Clip(message.PlaceName)}\" on Laneway. Sign up with this email to join."),
                new ButtonLink("Sign up", links.To(AppRoutes.SignUp, "email", message.Email)),
                new Paragraph("The invite lasts 30 days. If you don't know them, you can ignore this email."),
                new Paragraph("Don't want Laneway invites at this address? Unsubscribe and you won't get another."),
                new ButtonLink("Unsubscribe from invites", links.To(AppRoutes.UnsubscribeInvites, message.UnsubscribeToken)),
                new Footnote($"You got this email because @{username} entered this address on Laneway.")
            ]);
    }
}
