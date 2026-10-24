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
            ResetPasswordEmail.Render(MessageBodies.In<ResetPassword>(stored) with { Token = secret! }, links)
    };

    public static bool Has(MessageType type) => Templates.ContainsKey(type);

    public static EmailBody Render(MessageType type, JsonDocument stored, string? secret, AppLinks links) =>
        Templates[type](stored, secret, links);
}
