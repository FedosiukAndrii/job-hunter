using JobHunter.Application.Notifications;
using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

internal sealed partial class NotificationDispatchWorker(
    NotificationOutboxDispatcher dispatcher,
    TimeProvider timeProvider,
    IOptions<WorkerOptions> options,
    QuietHoursWindow quietHours,
    ILogger<NotificationDispatchWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.NotificationDispatchIntervalSeconds),
            timeProvider);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!quietHours.IsQuiet(timeProvider.GetUtcNow()))
                    {
                        var summary = await dispatcher.DispatchDueAsync(stoppingToken);
                        if (summary is
                        {
                            SentCount: > 0
                        }
                            or
                        {
                            RetryCount: > 0
                        }
                            or
                        {
                            UnknownCount: > 0
                        }
                            or
                        {
                            PermanentFailureCount: > 0
                        })
                        {
                            DispatchCompleted(
                                summary.SentCount,
                                summary.RetryCount,
                                summary.UnknownCount,
                                summary.PermanentFailureCount);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    DispatchFailed(
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
            DispatcherStopping();
        }
    }

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Information,
        Message = "Notification dispatch completed: sent={SentCount}, retries={RetryCount}, unknown={UnknownCount}, permanentFailures={PermanentFailureCount}.")]
    private partial void DispatchCompleted(
        int sentCount,
        int retryCount,
        int unknownCount,
        int permanentFailureCount);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Error,
        Message = "Notification dispatch failed with {ErrorType}: {Diagnostic}")]
    private partial void DispatchFailed(string errorType, string diagnostic);

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Information,
        Message = "Notification dispatcher is stopping.")]
    private partial void DispatcherStopping();

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
