using JobHunter.Application.Notifications;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Tests.Notifications;

public sealed class NotificationOutboxDispatcherTests
{
    [Fact]
    public async Task RetryExhaustionBecomesPermanentFailure()
    {
        var now = DateTimeOffset.Parse(
            "2026-09-16T15:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var store = new StubOutboxStore(CreateLease(attemptNumber: 1));
        var dispatcher = new NotificationOutboxDispatcher(
            [new StubChannel(NotificationSendResult.Retry("Transient", now))],
            store,
            new FixedTimeProvider(now),
            new NotificationOutboxDispatcherOptions
            {
                MaximumBatchSize = 1,
                MaximumAttempts = 1,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

        var summary = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.PermanentFailureCount);
        Assert.Equal(
            NotificationSendOutcome.PermanentFailure,
            store.CompletedResult!.Outcome);
        Assert.Equal("RetryLimitExceeded", store.CompletedResult.ErrorCode);
    }

    [Fact]
    public async Task TransientRetryUsesBoundedExponentialDelay()
    {
        var now = DateTimeOffset.Parse(
            "2026-09-16T15:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var lease = CreateLease(attemptNumber: 2);
        var store = new StubOutboxStore(lease);
        var dispatcher = new NotificationOutboxDispatcher(
            [new StubChannel(NotificationSendResult.Retry("Transient", now))],
            store,
            new FixedTimeProvider(now),
            new NotificationOutboxDispatcherOptions
            {
                MaximumBatchSize = 1,
                MaximumAttempts = 5,
                LeaseDuration = TimeSpan.FromMinutes(1),
                BaseRetryDelay = TimeSpan.FromMinutes(1),
                MaximumRetryDelay = TimeSpan.FromMinutes(10)
            });

        var summary = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.RetryCount);
        Assert.Equal(
            NotificationSendOutcome.TransientFailure,
            store.CompletedResult!.Outcome);
        Assert.InRange(
            store.CompletedResult.RetryAtUtc!.Value,
            now.AddSeconds(96),
            now.AddSeconds(144));
    }

    [Fact]
    public async Task RateLimitStopsBatchBeforeAnotherSendForDestination()
    {
        var now = DateTimeOffset.Parse(
            "2026-09-16T15:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var store = new QueueOutboxStore(
            [CreateLease(attemptNumber: 1), CreateLease(attemptNumber: 1)]);
        var channel = new SequenceChannel(
            [
                NotificationSendResult.Retry(
                    "TelegramRateLimited",
                    now.AddMinutes(2),
                    rateLimited: true),
                NotificationSendResult.Sent("unexpected")
            ]);
        var dispatcher = new NotificationOutboxDispatcher(
            [channel],
            store,
            new FixedTimeProvider(now),
            new NotificationOutboxDispatcherOptions
            {
                MaximumBatchSize = 2,
                MaximumAttempts = 5,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

        var summary = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.RetryCount);
        Assert.Equal(1, channel.SendCount);
        Assert.Equal(1, store.LeaseCount);
        Assert.Equal(
            now.AddMinutes(2),
            Assert.Single(store.CompletedResults).RetryAtUtc);
        Assert.Equal(
            now.AddMinutes(2),
            Assert.Single(store.DestinationRateLimitedUntilUtc));
    }

    [Fact]
    public async Task FinalRateLimitedAttemptCompletesPermanentlyAndPreservesCooldown()
    {
        var now = DateTimeOffset.Parse(
            "2026-09-16T15:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var retryAtUtc = now.AddMinutes(2);
        var store = new StubOutboxStore(CreateLease(attemptNumber: 1));
        var dispatcher = new NotificationOutboxDispatcher(
            [
                new StubChannel(
                    NotificationSendResult.Retry(
                        "TelegramRateLimited",
                        retryAtUtc,
                        rateLimited: true))
            ],
            store,
            new FixedTimeProvider(now),
            new NotificationOutboxDispatcherOptions
            {
                MaximumBatchSize = 1,
                MaximumAttempts = 1,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

        var summary = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.PermanentFailureCount);
        Assert.Equal(
            NotificationSendOutcome.PermanentFailure,
            store.CompletedResult?.Outcome);
        Assert.Equal("RetryLimitExceeded", store.CompletedResult?.ErrorCode);
        Assert.Equal(retryAtUtc, store.DestinationRateLimitedUntilUtc);
    }

    private static NotificationOutboxLease CreateLease(int attemptNumber) =>
        new(
            Guid.Parse("5a000000-0000-0000-0000-000000000001"),
            "telegram-test",
            "lease-token",
            Guid.NewGuid(),
            attemptNumber,
            new JobNotification(
                "Senior .NET Engineer",
                "Example",
                ["Remote"],
                WorkplaceMode.Remote,
                90,
                "rules-only",
                "Strong fit.",
                null,
                null,
                null,
                CompensationPeriod.Unknown,
                null,
                "https://jobs.dou.ua/vacancies/1/"));

    private sealed class StubOutboxStore(NotificationOutboxLease lease)
        : INotificationOutboxStore
    {
        private bool _leased;

        public NotificationSendResult? CompletedResult { get; private set; }

        public DateTimeOffset? DestinationRateLimitedUntilUtc { get; private set; }

        public Task<NotificationEnqueueOutcome> EnqueueAsync(
            NotificationIntent intent,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> RecoverExpiredLeasesAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<NotificationOutboxLease?> TryLeaseNextAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_leased)
            {
                return Task.FromResult<NotificationOutboxLease?>(null);
            }

            _leased = true;
            return Task.FromResult<NotificationOutboxLease?>(lease);
        }

        public Task CompleteAsync(
            NotificationOutboxLease completedLease,
            NotificationSendResult result,
            DateTimeOffset? destinationRateLimitedUntilUtc,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedResult = result;
            DestinationRateLimitedUntilUtc = destinationRateLimitedUntilUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class StubChannel(NotificationSendResult result)
        : INotificationChannel
    {
        public string DestinationId => "telegram-test";

        public TimeSpan MinimumSendInterval => TimeSpan.Zero;

        public Task<NotificationSendResult> SendAsync(
            JobNotification notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class QueueOutboxStore(
        IEnumerable<NotificationOutboxLease> leases)
        : INotificationOutboxStore
    {
        private readonly Queue<NotificationOutboxLease> _leases = new(leases);

        public int LeaseCount { get; private set; }

        public List<NotificationSendResult> CompletedResults { get; } = [];

        public List<DateTimeOffset?> DestinationRateLimitedUntilUtc { get; } = [];

        public Task<NotificationEnqueueOutcome> EnqueueAsync(
            NotificationIntent intent,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> RecoverExpiredLeasesAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<NotificationOutboxLease?> TryLeaseNextAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_leases.Count == 0)
            {
                return Task.FromResult<NotificationOutboxLease?>(null);
            }

            LeaseCount++;
            return Task.FromResult<NotificationOutboxLease?>(_leases.Dequeue());
        }

        public Task CompleteAsync(
            NotificationOutboxLease lease,
            NotificationSendResult result,
            DateTimeOffset? destinationRateLimitedUntilUtc,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedResults.Add(result);
            DestinationRateLimitedUntilUtc.Add(destinationRateLimitedUntilUtc);
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceChannel(
        IEnumerable<NotificationSendResult> results)
        : INotificationChannel
    {
        private readonly Queue<NotificationSendResult> _results = new(results);

        public string DestinationId => "telegram-test";

        public TimeSpan MinimumSendInterval => TimeSpan.Zero;

        public int SendCount { get; private set; }

        public Task<NotificationSendResult> SendAsync(
            JobNotification notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
