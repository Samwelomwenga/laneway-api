namespace Laneway.Api;

public enum MessageType
{
    PasswordChanged,
    VerifyEmail,
    ResetPassword,
    ConfirmEmailChange,
    EmailChanged,
    EmailChangeUndone
}

public enum MessageChannel
{
    Email
}
