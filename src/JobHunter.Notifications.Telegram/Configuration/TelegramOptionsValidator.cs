using JobHunter.Application.Security;
using Microsoft.Extensions.Options;

namespace JobHunter.Notifications.Telegram.Configuration;

internal sealed class TelegramOptionsValidator(ISecretReader secretReader)
    : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DestinationId)
            || options.DestinationId.Trim().Length > 256)
        {
            failures.Add(
                "Telegram:DestinationId must contain between 1 and 256 characters.");
        }

        if (options.PerChatMessagesPerSecond is <= 0 or > 1)
        {
            failures.Add(
                "Telegram:PerChatMessagesPerSecond must be greater than 0 and no greater than 1.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 120)
        {
            failures.Add(
                "Telegram:RequestTimeoutSeconds must be between 1 and 120.");
        }

        if (options.TransientRetrySeconds is < 1 or > 3600)
        {
            failures.Add(
                "Telegram:TransientRetrySeconds must be between 1 and 3600.");
        }

        if (options.Enabled)
        {
            if (secretReader.GetSecret(TelegramOptions.BotTokenConfigurationKey) is null)
            {
                failures.Add(
                    "Telegram is enabled but Telegram:BotToken is unavailable from the configured secret provider.");
            }

            var chatId = secretReader.GetSecret(TelegramOptions.ChatIdConfigurationKey);
            if (!long.TryParse(
                    chatId,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedChatId)
                || parsedChatId == 0)
            {
                failures.Add(
                    "Telegram is enabled but Telegram:ChatId is unavailable or invalid in the configured secret provider.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
