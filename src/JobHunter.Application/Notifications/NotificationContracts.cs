using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Notifications;

public interface INotificationDestinationProvider
{
    IReadOnlyList<NotificationDestination> GetDestinations();
}

public sealed record NotificationDestination
{
    public NotificationDestination(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim();
        if (normalized.Length > 256)
        {
            throw new ArgumentException(
                "A notification destination ID must contain at most 256 characters.",
                nameof(id));
        }

        Id = normalized;
    }

    public string Id { get; }
}

public sealed record JobNotification(
    string Title,
    string Company,
    IReadOnlyList<string> Locations,
    WorkplaceMode WorkplaceMode,
    int Score,
    string ScoreMode,
    string Summary,
    decimal? CompensationMinimum,
    decimal? CompensationMaximum,
    string? CompensationCurrency,
    CompensationPeriod CompensationPeriod,
    DateTimeOffset? PublishedAtUtc,
    string CanonicalUrl);

public sealed record NotificationIntent(
    string DestinationId,
    Guid JobId,
    int NotificationVersion,
    JobNotification Payload);

public enum NotificationEnqueueOutcome
{
    Created = 0,
    AlreadyExists = 1,
    DestinationDisabled = 2,
    QueueFull = 3
}

public interface INotificationOutboxStore
{
    Task<NotificationEnqueueOutcome> EnqueueAsync(
        NotificationIntent intent,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> RecoverExpiredLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<NotificationOutboxLease?> TryLeaseNextAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        NotificationOutboxLease lease,
        NotificationSendResult result,
        DateTimeOffset? destinationRateLimitedUntilUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface INotificationDestinationStateStore
{
    Task<NotificationDestinationStateSnapshot?> GetAsync(
        string destinationId,
        CancellationToken cancellationToken);

    Task SetDestinationEnabledAsync(
        string destinationId,
        bool enabled,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record NotificationDestinationStateSnapshot(
    string DestinationId,
    bool IsEnabled,
    string? FailureCode,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationOutboxLease(
    Guid OutboxId,
    string DestinationId,
    string LeaseToken,
    Guid DeliveryAttemptId,
    int AttemptNumber,
    JobNotification Payload);

public interface INotificationChannel
{
    string DestinationId { get; }

    TimeSpan MinimumSendInterval { get; }

    Task<NotificationSendResult> SendAsync(
        JobNotification notification,
        CancellationToken cancellationToken);
}

public enum NotificationSendOutcome
{
    Sent = 0,
    TransientFailure = 1,
    RateLimited = 2,
    Unknown = 3,
    PermanentFailure = 4
}

public sealed record NotificationSendResult(
    NotificationSendOutcome Outcome,
    string? ExternalMessageId,
    string? ErrorCode,
    DateTimeOffset? RetryAtUtc,
    bool DisableDestination)
{
    public static NotificationSendResult Sent(string externalMessageId) =>
        new(NotificationSendOutcome.Sent, externalMessageId, null, null, false);

    public static NotificationSendResult Retry(
        string errorCode,
        DateTimeOffset retryAtUtc,
        bool rateLimited = false) =>
        new(
            rateLimited
                ? NotificationSendOutcome.RateLimited
                : NotificationSendOutcome.TransientFailure,
            null,
            errorCode,
            retryAtUtc,
            false);

    public static NotificationSendResult Unknown(string errorCode) =>
        new(NotificationSendOutcome.Unknown, null, errorCode, null, false);

    public static NotificationSendResult PermanentFailure(
        string errorCode,
        bool disableDestination = false) =>
        new(
            NotificationSendOutcome.PermanentFailure,
            null,
            errorCode,
            null,
            disableDestination);
}
