using JobHunter.Application.Orchestration;

namespace JobHunter.Worker;

public sealed partial class Worker(
    ILogger<Worker> logger,
    TimeProvider timeProvider,
    ScanOrchestrator scanOrchestrator,
    Microsoft.Extensions.Options.IOptions<WorkerOptions> options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromSeconds(options.Value.SchedulerTickSeconds);
        using var timer = new PeriodicTimer(tickInterval, timeProvider);

        SchedulerStarted(options.Value.SchedulerTickSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var summary = await scanOrchestrator.RunDueAsync(stoppingToken);
                    if (summary.DueCount > 0)
                    {
                        SchedulerTickCompleted(
                            summary.DueCount,
                            summary.StartedCount,
                            summary.SucceededCount,
                            summary.PartialCount,
                            summary.BlockedCount,
                            summary.FailedCount,
                            summary.ObservedCount,
                            summary.NotificationIntentCount,
                            summary.SuppressedNotificationIntentCount,
                            summary.AiAnalysisCount,
                            summary.AcceptedAiAnalysisCount,
                            summary.DeferredAiAnalysisCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    SchedulerTickFailed(
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
            SchedulerStopping();
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Job Hunter scheduler started with a {SchedulerTickSeconds}-second tick interval.")]
    private partial void SchedulerStarted(int schedulerTickSeconds);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Scheduler tick completed: due={DueCount}, started={StartedCount}, succeeded={SucceededCount}, partial={PartialCount}, blocked={BlockedCount}, failed={FailedCount}, observed={ObservedCount}, intents={NotificationIntentCount}, suppressedIntents={SuppressedNotificationIntentCount}, aiAnalyses={AiAnalysisCount}, acceptedAi={AcceptedAiAnalysisCount}, deferredAi={DeferredAiAnalysisCount}.")]
    private partial void SchedulerTickCompleted(
        int dueCount,
        int startedCount,
        int succeededCount,
        int partialCount,
        int blockedCount,
        int failedCount,
        int observedCount,
        int notificationIntentCount,
        int suppressedNotificationIntentCount,
        int aiAnalysisCount,
        int acceptedAiAnalysisCount,
        int deferredAiAnalysisCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Job Hunter scheduler is stopping.")]
    private partial void SchedulerStopping();

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Scheduler tick failed with {ErrorType}: {Diagnostic}")]
    private partial void SchedulerTickFailed(string errorType, string diagnostic);

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
