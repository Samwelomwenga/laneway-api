using Microsoft.Extensions.Options;

namespace Laneway.Api;

public static class MessageSetup
{
    public static IServiceCollection AddMessages(this IServiceCollection services)
    {
        services.AddOptions<BrevoOptions>().BindConfiguration(BrevoOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<BrevoOptions>, BrevoOptionsValidator>();
        services.AddOptions<WebAppOptions>().BindConfiguration(WebAppOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<WebAppOptions>, WebAppOptionsValidator>();

        services.AddSingleton<AppLinks>();
        services.AddSingleton<DeliverySignal>();
        services.AddSingleton<DeliveryCommitPoke>();
        services.AddScoped<Messages>();
        services.AddHttpClient<IEmailProvider, BrevoEmail>(BrevoEmail.Configure).RemoveAllLoggers();
        services.AddHostedService<DeliveryWorker>();

        return services;
    }
}
