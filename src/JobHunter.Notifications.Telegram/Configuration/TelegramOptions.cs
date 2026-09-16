namespace JobHunter.Notifications.Telegram.Configuration;

public sealed class TelegramOptions
{
    private string _destinationId = "telegram-primary";

    public const string SectionName = "Telegram";

    public const string BotTokenConfigurationKey = "Telegram:BotToken";

    public const string ChatIdConfigurationKey = "Telegram:ChatId";

    public bool Enabled { get; set; }

    public string DestinationId
    {
        get => _destinationId;
        set => _destinationId = value?.Trim() ?? string.Empty;
    }

    public double PerChatMessagesPerSecond { get; set; } = 1;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int TransientRetrySeconds { get; set; } = 60;
}
