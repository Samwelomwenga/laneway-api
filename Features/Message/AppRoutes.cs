using Microsoft.Extensions.Options;

namespace Laneway.Api;

public static class AppRoutes
{
    public const string ForgotPassword = "/forgot-password";

    public const string VerifyEmail = "/verify-email";

    public const string ResetPassword = "/reset-password";

    public const string ConfirmEmailChange = "/confirm-email-change";

    public const string UndoEmailChange = "/undo-email-change";

    public const string SignUp = "/sign-up";

    public const string UnsubscribeInvites = "/unsubscribe-invites";
}

public sealed class AppLinks(IOptions<WebAppOptions> options)
{
    public Uri To(string route) => new($"{options.Value.BaseUrl}{route}");

    public Uri To(string route, string token) => To(route, "token", token);

    public Uri To(string route, string name, string value) =>
        new($"{options.Value.BaseUrl}{route}#{name}={Uri.EscapeDataString(value)}");
}
