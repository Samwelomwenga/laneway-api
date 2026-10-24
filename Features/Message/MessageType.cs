namespace Laneway.Api;

public enum MessageType
{
    PasswordChanged,
    VerifyEmail,
    ResetPassword,
    ConfirmEmailChange,
    EmailChanged
}

public enum MessageChannel
{
    Email
}
