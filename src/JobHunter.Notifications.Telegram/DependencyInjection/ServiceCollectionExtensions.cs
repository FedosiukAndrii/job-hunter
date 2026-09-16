using JobHunter.Application.Notifications;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.Notifications.Telegram.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();
        services.AddSingleton<
            INotificationDestinationProvider,
            TelegramNotificationDestinationProvider>();
        services.AddHttpClient<TelegramNotificationChannel>(
                client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });
        services.AddHttpClient<ITelegramSetupService, TelegramSetupService>(
                client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });

        if (configuration.GetValue<bool>($"{TelegramOptions.SectionName}:Enabled"))
        {
            services.AddSingleton<INotificationChannel>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<TelegramNotificationChannel>());
        }

        return services;
    }
}
