using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

internal sealed partial class RetentionWorker(
    IDataRetentionService retentionService,
    TimeProvider timeProvider,
    IOptions<RetentionOptions> options,
    ILogger<RetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredOptions = options.Value;
        var interval = TimeSpan.FromHours(configuredOptions.CleanupIntervalHours);
        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cutoff = timeProvider.GetUtcNow()
                        .AddDays(-configuredOptions.OperationalRecordDays);
                    var summary = await retentionService.CleanupAsync(
                        cutoff,
                        stoppingToken);
                    CleanupCompleted(
                        summary.SourceRunCount,
                        summary.ObservationCount,
                        summary.DeliveryAttemptCount,
                        summary.ScrubbedOutboxCount,
                        summary.ApplicationEventCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    CleanupFailed(
                        exception.GetType().Name,
                        Bound(exception.Message, 512));
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            CleanupStopping();
        }
    }

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Information,
        Message = "Retention cleanup completed: sourceRuns={SourceRunCount}, observations={ObservationCount}, deliveryAttempts={DeliveryAttemptCount}, scrubbedOutbox={ScrubbedOutboxCount}, applicationEvents={ApplicationEventCount}.")]
    private partial void CleanupCompleted(
        int sourceRunCount,
        int observationCount,
        int deliveryAttemptCount,
        int scrubbedOutboxCount,
        int applicationEventCount);

    [LoggerMessage(
        EventId = 31,
        Level = LogLevel.Error,
        Message = "Retention cleanup failed with {ErrorType}: {Diagnostic}")]
    private partial void CleanupFailed(string errorType, string diagnostic);

    [LoggerMessage(
        EventId = 32,
        Level = LogLevel.Information,
        Message = "Retention worker is stopping.")]
    private partial void CleanupStopping();

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
