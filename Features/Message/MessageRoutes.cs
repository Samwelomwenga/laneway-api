namespace Laneway.Api;

public sealed record MessageRoute(IReadOnlyList<MessageChannel> Channels, bool EmailByNature, bool OptOut, TimeSpan SendBy);

public static class MessageRoutes
{
    public static IReadOnlyDictionary<MessageType, MessageRoute> All { get; } = new Dictionary<MessageType, MessageRoute>
    {
        [MessageType.PasswordChanged] = new([MessageChannel.Email], EmailByNature: false, OptOut: false, TimeSpan.FromHours(24)),
        [MessageType.VerifyEmail] = new([MessageChannel.Email], EmailByNature: true, OptOut: false, AuthToken.VerificationLifetime),
        [MessageType.ResetPassword] = new([MessageChannel.Email], EmailByNature: true, OptOut: false, AuthToken.ResetLifetime),
        [MessageType.ConfirmEmailChange] = new([MessageChannel.Email], EmailByNature: true, OptOut: false, AuthToken.ChangeEmailLifetime),
        [MessageType.EmailChanged] = new([MessageChannel.Email], EmailByNature: true, OptOut: false, TimeSpan.FromHours(24)),
        [MessageType.EmailChangeUndone] = new([MessageChannel.Email], EmailByNature: true, OptOut: false, TimeSpan.FromHours(24))
    };

    public static MessageRoute For(MessageType type) => All[type];
}
