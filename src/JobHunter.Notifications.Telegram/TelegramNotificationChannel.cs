using System.Globalization;
using JobHunter.Application.Notifications;
using JobHunter.Application.Security;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace JobHunter.Notifications.Telegram;

public sealed class TelegramNotificationChannel(
    HttpClient httpClient,
    ISecretReader secretReader,
    TimeProvider timeProvider,
    IOptions<TelegramOptions> options)
    : INotificationChannel
{
    public string DestinationId => options.Value.DestinationId;

    public TimeSpan MinimumSendInterval =>
        TimeSpan.FromSeconds(1 / options.Value.PerChatMessagesPerSecond);

    public async Task<NotificationSendResult> SendAsync(
        JobNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var token = secretReader.GetSecret(TelegramOptions.BotTokenConfigurationKey);
        var chatIdValue = secretReader.GetSecret(TelegramOptions.ChatIdConfigurationKey);
        if (token is null)
        {
            return NotificationSendResult.PermanentFailure("TelegramBotTokenMissing");
        }

        if (!long.TryParse(
                chatIdValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var chatId)
            || chatId == 0)
        {
            return NotificationSendResult.PermanentFailure("TelegramChatIdInvalid");
        }

        string messageText;
        try
        {
            messageText = TelegramMessageRenderer.Render(
                notification,
                timeProvider.GetUtcNow(),
                options.Value.DebugMode);
        }
        catch (InvalidDataException)
        {
            return NotificationSendResult.PermanentFailure("TelegramPayloadInvalid");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        try
        {
            var botClient = new TelegramBotClient(token, httpClient);
            var message = await botClient.SendMessage(
                chatId,
                messageText,
                parseMode: ParseMode.Html,
                cancellationToken: timeout.Token);
            return NotificationSendResult.Sent(
                message.Id.ToString(CultureInfo.InvariantCulture));
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 429)
        {
            var retryAfterSeconds = exception.Parameters?.RetryAfter
                ?? options.Value.TransientRetrySeconds;
            return NotificationSendResult.Retry(
                "TelegramRateLimited",
                timeProvider.GetUtcNow().AddSeconds(retryAfterSeconds),
                rateLimited: true);
        }
        catch (ApiRequestException exception) when (exception.ErrorCode >= 500)
        {
            return NotificationSendResult.Retry(
                $"TelegramHttp{exception.ErrorCode}",
                timeProvider.GetUtcNow().AddSeconds(options.Value.TransientRetrySeconds));
        }
        catch (ApiRequestException exception)
            when (exception.ErrorCode is 400 or 401 or 403)
        {
            return NotificationSendResult.PermanentFailure(
                $"TelegramHttp{exception.ErrorCode}",
                disableDestination: true);
        }
        catch (HttpRequestException)
        {
            return NotificationSendResult.Retry(
                "TelegramTransportFailure",
                timeProvider.GetUtcNow().AddSeconds(options.Value.TransientRetrySeconds));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NotificationSendResult.Unknown("TelegramSendTimeout");
        }
        catch (ArgumentException)
        {
            return NotificationSendResult.PermanentFailure("TelegramBotTokenInvalid");
        }
    }
}
