using System.Text.Json;

namespace Laneway.Api;

public static class EmailTemplates
{
    private static readonly Dictionary<MessageType, Func<JsonDocument, string?, AppLinks, EmailBody>> Templates = new()
    {
        [MessageType.PasswordChanged] = (stored, _, links) =>
            PasswordChangedEmail.Render(MessageBodies.In<PasswordChanged>(stored), links),
        [MessageType.VerifyEmail] = (stored, secret, links) =>
            VerifyEmailEmail.Render(MessageBodies.In<VerifyEmail>(stored) with { Token = secret! }, links),
        [MessageType.ResetPassword] = (stored, secret, links) =>
            ResetPasswordEmail.Render(MessageBodies.In<ResetPassword>(stored) with { Token = secret! }, links),
        [MessageType.ConfirmEmailChange] = (stored, secret, links) =>
            ConfirmEmailChangeEmail.Render(MessageBodies.In<ConfirmEmailChange>(stored) with { Token = secret! }, links),
        [MessageType.EmailChanged] = (stored, secret, links) =>
            EmailChangedEmail.Render(MessageBodies.In<EmailChanged>(stored) with { Token = secret! }, links),
        [MessageType.EmailChangeUndone] = (stored, _, _) =>
            EmailChangeUndoneEmail.Render(MessageBodies.In<EmailChangeUndone>(stored)),
        [MessageType.InviteSent] = (stored, secret, links) =>
            InviteSentEmail.Render(MessageBodies.In<InviteSent>(stored) with { UnsubscribeToken = secret! }, links)
    };

    public static bool Has(MessageType type) => Templates.ContainsKey(type);

    public static EmailBody Render(MessageType type, JsonDocument stored, string? secret, AppLinks links) =>
        Templates[type](stored, secret, links);
}
