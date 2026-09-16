using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.IntegrationTests;

public sealed class RetentionWorkerTests
{
    [Fact]
    public async Task CleanupFailureIsContainedUntilTheNextScheduledAttempt()
    {
        var retention = new FailingRetentionService();
        var worker = new RetentionWorker(
            retention,
            TimeProvider.System,
            Options.Create(
                new RetentionOptions
                {
                    OperationalRecordDays = 30,
                    CleanupIntervalHours = 1
                }),
            NullLogger<RetentionWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await retention.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, retention.CallCount);
    }

    private sealed class FailingRetentionService : IDataRetentionService
    {
        public TaskCompletionSource Attempted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task<DataRetentionSummary> CleanupAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Attempted.TrySetResult();
            throw new IOException("The SQLite fixture failed.");
        }
    }
}
