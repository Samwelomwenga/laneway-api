using System.Text.Json;

namespace Laneway.Api;

public static class EmailTemplates
{
    private static readonly Dictionary<MessageType, Func<JsonDocument, AppLinks, EmailBody>> Templates = new()
    {
        [MessageType.PasswordChanged] = (stored, links) =>
            PasswordChangedEmail.Render(MessageBodies.In<PasswordChanged>(stored), links)
    };

    public static bool Has(MessageType type) => Templates.ContainsKey(type);

    public static EmailBody Render(MessageType type, JsonDocument stored, AppLinks links) =>
        Templates[type](stored, links);
}
