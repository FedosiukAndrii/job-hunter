using JobHunter.Application.Notifications;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Notifications.Telegram;

internal sealed class TelegramNotificationDestinationProvider(
    IOptions<TelegramOptions> options)
    : INotificationDestinationProvider
{
    public IReadOnlyList<NotificationDestination> GetDestinations() =>
        options.Value.Enabled
            ? [new NotificationDestination(
                options.Value.DestinationId,
                suppressPossibleDuplicateNotifications: true)]
            : [];
}
