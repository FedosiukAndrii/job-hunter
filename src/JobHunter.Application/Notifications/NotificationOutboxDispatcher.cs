namespace JobHunter.Application.Notifications;

public sealed class NotificationOutboxDispatcher
{
    private readonly Dictionary<string, INotificationChannel> _channels;
    private readonly Dictionary<string, DateTimeOffset> _nextAllowedSendAtUtc = [];
    private readonly INotificationOutboxStore _outboxStore;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationOutboxDispatcherOptions _options;

    public NotificationOutboxDispatcher(
        IEnumerable<INotificationChannel> channels,
        INotificationOutboxStore outboxStore,
        TimeProvider timeProvider,
        NotificationOutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _channels = channels.ToDictionary(
            channel => channel.DestinationId,
            StringComparer.Ordinal);
        _outboxStore = outboxStore;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task<NotificationDispatchSummary> DispatchDueAsync(
        CancellationToken cancellationToken)
    {
        var recovered = await _outboxStore.RecoverExpiredLeasesAsync(
            _timeProvider.GetUtcNow(),
            cancellationToken);
        var sent = 0;
        var retried = 0;
        var unknown = recovered;
        var permanentFailures = 0;

        for (var index = 0; index < _options.MaximumBatchSize; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await _outboxStore.TryLeaseNextAsync(
                _timeProvider.GetUtcNow(),
                _options.LeaseDuration,
                cancellationToken);
            if (lease is null)
            {
                break;
            }

            var result = await SendAsync(lease, cancellationToken);
            var completedAtUtc = _timeProvider.GetUtcNow();
            DateTimeOffset? destinationRateLimitedUntilUtc = null;
            if (result.Outcome == NotificationSendOutcome.RateLimited)
            {
                destinationRateLimitedUntilUtc = NormalizeRateLimit(
                    result,
                    completedAtUtc);
                result = result with
                {
                    RetryAtUtc = destinationRateLimitedUntilUtc
                };
                ApplyDestinationRateLimit(
                    lease.DestinationId,
                    destinationRateLimitedUntilUtc.Value);
            }

            result = ApplyRetryPolicy(lease, result, completedAtUtc);
            await _outboxStore.CompleteAsync(
                lease,
                result,
                destinationRateLimitedUntilUtc,
                completedAtUtc,
                CancellationToken.None);

            switch (result.Outcome)
            {
                case NotificationSendOutcome.Sent:
                    sent++;
                    break;
                case NotificationSendOutcome.TransientFailure:
                case NotificationSendOutcome.RateLimited:
                    retried++;
                    break;
                case NotificationSendOutcome.Unknown:
                    unknown++;
                    break;
                case NotificationSendOutcome.PermanentFailure:
                    permanentFailures++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported notification outcome '{result.Outcome}'.");
            }

            if (destinationRateLimitedUntilUtc is not null)
            {
                break;
            }
        }

        return new NotificationDispatchSummary(
            sent,
            retried,
            unknown,
            permanentFailures);
    }

    private async Task<NotificationSendResult> SendAsync(
        NotificationOutboxLease lease,
        CancellationToken cancellationToken)
    {
        if (!_channels.TryGetValue(lease.DestinationId, out var channel))
        {
            return NotificationSendResult.PermanentFailure("DestinationUnavailable");
        }

        if (_nextAllowedSendAtUtc.TryGetValue(
                channel.DestinationId,
                out var nextAllowedAtUtc))
        {
            var delay = nextAllowedAtUtc - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken);
            }
        }

        var attemptedAtUtc = _timeProvider.GetUtcNow();
        _nextAllowedSendAtUtc[channel.DestinationId] =
            attemptedAtUtc.Add(channel.MinimumSendInterval);

        try
        {
            return await channel.SendAsync(lease.Payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NotificationSendResult.Unknown("DispatchCancelled");
        }
        catch (Exception exception)
        {
            return NotificationSendResult.Unknown(exception.GetType().Name);
        }
    }

    private void ApplyDestinationRateLimit(
        string destinationId,
        DateTimeOffset retryAtUtc)
    {
        if (!_nextAllowedSendAtUtc.TryGetValue(
                destinationId,
                out var existingRetryAtUtc)
            || retryAtUtc > existingRetryAtUtc)
        {
            _nextAllowedSendAtUtc[destinationId] = retryAtUtc;
        }
    }

    private static DateTimeOffset NormalizeRateLimit(
        NotificationSendResult result,
        DateTimeOffset now)
    {
        var retryAtUtc = result.RetryAtUtc
            ?? throw new InvalidOperationException(
                "A rate-limited result must include RetryAtUtc.");
        return retryAtUtc < now ? now : retryAtUtc;
    }

    private NotificationSendResult ApplyRetryPolicy(
        NotificationOutboxLease lease,
        NotificationSendResult result,
        DateTimeOffset now)
    {
        if (result.Outcome is not (
                NotificationSendOutcome.TransientFailure
                or NotificationSendOutcome.RateLimited))
        {
            return result;
        }

        if (lease.AttemptNumber >= _options.MaximumAttempts)
        {
            return NotificationSendResult.PermanentFailure("RetryLimitExceeded");
        }

        if (result.Outcome == NotificationSendOutcome.RateLimited)
        {
            return result;
        }

        var exponent = Math.Min(lease.AttemptNumber - 1, 10);
        var delayTicks = _options.BaseRetryDelay.Ticks * (1L << exponent);
        var cappedTicks = Math.Min(delayTicks, _options.MaximumRetryDelay.Ticks);
        var jitter = 0.8
            + lease.OutboxId.ToByteArray()[0] / 255d * 0.4;
        var retryAtUtc = now.AddTicks((long)(cappedTicks * jitter));
        if (result.RetryAtUtc is not null && result.RetryAtUtc > retryAtUtc)
        {
            retryAtUtc = result.RetryAtUtc.Value;
        }

        return result with { RetryAtUtc = retryAtUtc };
    }
}

public sealed class NotificationOutboxDispatcherOptions
{
    public int MaximumBatchSize { get; init; } = 20;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    public int MaximumAttempts { get; init; } = 5;

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(30);

    internal void Validate()
    {
        if (MaximumBatchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBatchSize),
                MaximumBatchSize,
                "The outbox batch size must be between 1 and 1000.");
        }

        if (LeaseDuration < TimeSpan.FromSeconds(5)
            || LeaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(LeaseDuration),
                LeaseDuration,
                "The outbox lease duration must be between 5 seconds and 30 minutes.");
        }

        if (MaximumAttempts is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAttempts),
                MaximumAttempts,
                "The maximum notification attempt count must be between 1 and 20.");
        }

        if (BaseRetryDelay < TimeSpan.FromSeconds(1)
            || MaximumRetryDelay < BaseRetryDelay
            || MaximumRetryDelay > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(BaseRetryDelay),
                BaseRetryDelay,
                "Notification retry delays must be ordered, positive, and capped at 24 hours.");
        }
    }
}

public sealed record NotificationDispatchSummary(
    int SentCount,
    int RetryCount,
    int UnknownCount,
    int PermanentFailureCount);
