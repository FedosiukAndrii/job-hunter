using JobHunter.Application.Orchestration;
using JobHunter.Domain.Sources;

namespace JobHunter.Worker;

internal sealed partial class RunOnceCompletionService(
    WorkerCommand command,
    ScanOrchestrator scanOrchestrator,
    IHostApplicationLifetime applicationLifetime,
    ILogger<RunOnceCompletionService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var source = SourceName.Create(
            command.Source
                ?? throw new InvalidOperationException(
                    "The run-once command requires a source."));
        var summary = await scanOrchestrator.RunOnceAsync(
            source,
            cancellationToken);
        EnsureSuccessful(source, summary);

        RunCompleted(
            source.Value,
            summary.SucceededCount,
            summary.PartialCount,
            summary.BlockedCount,
            summary.FailedCount,
            summary.ObservedCount,
            summary.NotificationIntentCount,
            summary.AiFallbackCount);
        applicationLifetime.StopApplication();
    }

    internal static void EnsureSuccessful(
        SourceName source,
        ScanBatchSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.DueCount == 0)
        {
            throw new OperatorCommandException(
                $"Source '{source}' has no enabled, runnable subscription.");
        }

        if (summary.SucceededCount != summary.DueCount)
        {
            throw new OperatorCommandException(
                $"Source '{source}' did not complete successfully: "
                + $"started={summary.StartedCount}, "
                + $"succeeded={summary.SucceededCount}, "
                + $"partial={summary.PartialCount}, "
                + $"blocked={summary.BlockedCount}, "
                + $"failed={summary.FailedCount}.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 24,
        Level = LogLevel.Information,
        Message = "One-time source scan completed: source={Source}, succeeded={SucceededCount}, partial={PartialCount}, blocked={BlockedCount}, failed={FailedCount}, observed={ObservedCount}, intents={NotificationIntentCount}, aiFallbacks={AiFallbackCount}.")]
    private partial void RunCompleted(
        string source,
        int succeededCount,
        int partialCount,
        int blockedCount,
        int failedCount,
        int observedCount,
        int notificationIntentCount,
        int aiFallbackCount);
}
