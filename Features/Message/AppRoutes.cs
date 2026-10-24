using Microsoft.Extensions.Options;

namespace Laneway.Api;

public static class AppRoutes
{
    public const string ForgotPassword = "/forgot-password";

    public const string VerifyEmail = "/verify-email";
}

public sealed class AppLinks(IOptions<WebAppOptions> options)
{
    public Uri To(string route) => new($"{options.Value.BaseUrl}{route}");

    public Uri To(string route, string token) =>
        new($"{options.Value.BaseUrl}{route}#token={Uri.EscapeDataString(token)}");
}
