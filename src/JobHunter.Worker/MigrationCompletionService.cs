namespace JobHunter.Worker;

internal sealed partial class MigrationCompletionService(
    IHostApplicationLifetime applicationLifetime,
    ILogger<MigrationCompletionService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        MigrationCompleted();
        applicationLifetime.StopApplication();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Database migration completed successfully.")]
    private partial void MigrationCompleted();
}
