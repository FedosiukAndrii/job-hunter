using JobHunter.Notifications.Telegram;

namespace JobHunter.Worker;

internal sealed partial class TelegramSetupCompletionService(
    ITelegramSetupService telegramSetupService,
    IHostApplicationLifetime applicationLifetime,
    ILogger<TelegramSetupCompletionService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await telegramSetupService.ValidateAsync(cancellationToken);
        SetupCompleted();
        applicationLifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 23,
        Level = LogLevel.Information,
        Message = "Telegram bot and private destination validated; a test message was sent.")]
    private partial void SetupCompleted();
}
