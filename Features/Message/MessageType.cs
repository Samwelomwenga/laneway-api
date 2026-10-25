namespace Laneway.Api;

public enum MessageType
{
    PasswordChanged,
    VerifyEmail,
    ResetPassword,
    ConfirmEmailChange,
    EmailChanged,
    EmailChangeUndone,
    InviteSent
}

public enum MessageChannel
{
    Email
}
