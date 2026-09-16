using System.Globalization;
using JobHunter.Application.Notifications;
using JobHunter.Application.Security;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace JobHunter.Notifications.Telegram;

public interface ITelegramSetupService
{
    Task<TelegramSetupResult> CheckAsync(CancellationToken cancellationToken);

    Task<TelegramSetupResult> ValidateAsync(CancellationToken cancellationToken);
}

public sealed record TelegramSetupResult(string BotUsername);

public sealed class TelegramSetupException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class TelegramSetupService(
    HttpClient httpClient,
    ISecretReader secretReader,
    INotificationDestinationStateStore destinationStateStore,
    TimeProvider timeProvider,
    IOptions<TelegramOptions> options)
    : ITelegramSetupService
{
    public Task<TelegramSetupResult> CheckAsync(
        CancellationToken cancellationToken) =>
        CheckCoreAsync(sendTestMessage: false, cancellationToken);

    public Task<TelegramSetupResult> ValidateAsync(
        CancellationToken cancellationToken) =>
        CheckCoreAsync(sendTestMessage: true, cancellationToken);

    private async Task<TelegramSetupResult> CheckCoreAsync(
        bool sendTestMessage,
        CancellationToken cancellationToken)
    {
        var token = secretReader.GetSecret(TelegramOptions.BotTokenConfigurationKey)
            ?? throw new TelegramSetupException(
                "Telegram bot token is unavailable from the configured secret provider.");
        var chatIdValue = secretReader.GetSecret(TelegramOptions.ChatIdConfigurationKey);
        if (!long.TryParse(
                chatIdValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var chatId)
            || chatId == 0)
        {
            throw new TelegramSetupException(
                "Telegram chat ID is unavailable or invalid in the configured secret provider.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        try
        {
            var botClient = new TelegramBotClient(token, httpClient);
            var bot = await botClient.GetMe(timeout.Token);
            var chat = await botClient.GetChat(chatId, timeout.Token);
            if (chat.Type != ChatType.Private)
            {
                throw new TelegramSetupException(
                    "The configured Telegram destination must be a private chat.");
            }

            if (sendTestMessage)
            {
                await botClient.SendMessage(
                    chatId,
                    "Job Hunter setup succeeded. Notifications are ready.",
                    cancellationToken: timeout.Token);
                await destinationStateStore.SetDestinationEnabledAsync(
                    options.Value.DestinationId,
                    enabled: true,
                    failureCode: null,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            return new TelegramSetupResult(bot.Username ?? "configured bot");
        }
        catch (TelegramSetupException)
        {
            throw;
        }
        catch (ApiRequestException exception)
        {
            throw new TelegramSetupException(
                $"Telegram setup failed with API status {exception.ErrorCode}.");
        }
        catch (HttpRequestException)
        {
            throw new TelegramSetupException(
                "Telegram setup could not reach the Bot API.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TelegramSetupException(
                "Telegram setup exceeded its configured timeout.");
        }
        catch (ArgumentException)
        {
            throw new TelegramSetupException(
                "Telegram setup found an invalid bot token.");
        }
    }
}
