namespace JobHunter.Worker;

public sealed partial class Worker(
    ILogger<Worker> logger,
    TimeProvider timeProvider,
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
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var timestampUtc = timeProvider.GetUtcNow();
                    SchedulerTick(timestampUtc);
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
        Level = LogLevel.Debug,
        Message = "Scheduler tick at {TimestampUtc}. Source orchestration is not enabled yet.")]
    private partial void SchedulerTick(DateTimeOffset timestampUtc);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Job Hunter scheduler is stopping.")]
    private partial void SchedulerStopping();
}
