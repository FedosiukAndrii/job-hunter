using JobHunter.Domain.Common;

namespace JobHunter.Domain.Notifications;

public sealed class NotificationOutbox : IConcurrencyTracked
{
    private NotificationOutbox()
    {
    }

    public Guid Id { get; private set; }

    public string DestinationId { get; private set; } = string.Empty;

    public Guid JobId { get; private set; }

    public int NotificationVersion { get; private set; }

    public OutboxStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public string? LeaseToken { get; private set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public int AttemptCount { get; private set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<DeliveryAttempt> DeliveryAttempts { get; } = [];

    public static NotificationOutbox Create(
        string destinationId,
        Guid jobId,
        int notificationVersion,
        string payloadJson,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationId);
        if (destinationId.Trim().Length > 256)
        {
            throw new ArgumentException(
                "A notification destination ID must contain at most 256 characters.",
                nameof(destinationId));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A job ID is required.", nameof(jobId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(notificationVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        return new NotificationOutbox
        {
            Id = Guid.NewGuid(),
            DestinationId = destinationId.Trim(),
            JobId = jobId,
            NotificationVersion = notificationVersion,
            Status = OutboxStatus.Pending,
            CreatedAtUtc = now,
            NextAttemptAtUtc = now,
            PayloadJson = payloadJson
        };
    }

    public DeliveryAttempt Lease(DateTimeOffset now, TimeSpan leaseDuration)
    {
        if (Status != OutboxStatus.Pending || NextAttemptAtUtc > now)
        {
            throw new InvalidOperationException("The notification is not ready to be leased.");
        }

        if (leaseDuration < TimeSpan.FromSeconds(5)
            || leaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                leaseDuration,
                "A notification lease must be between 5 seconds and 30 minutes.");
        }

        Status = OutboxStatus.Leased;
        LeaseToken = Guid.NewGuid().ToString("N");
        LeaseExpiresAtUtc = now.Add(leaseDuration);
        AttemptCount = checked(AttemptCount + 1);

        var attempt = DeliveryAttempt.Start(Id, AttemptCount, now);
        DeliveryAttempts.Add(attempt);
        return attempt;
    }

    public void MarkSent(
        string leaseToken,
        Guid deliveryAttemptId,
        string externalMessageId,
        DateTimeOffset now)
    {
        var attempt = GetActiveAttempt(leaseToken, deliveryAttemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalMessageId);
        attempt.Complete("Sent", null, externalMessageId, now);
        Status = OutboxStatus.Sent;
        SentAtUtc = now;
        ClearLease();
    }

    public void Requeue(
        string leaseToken,
        Guid deliveryAttemptId,
        DateTimeOffset retryAtUtc,
        string errorCode,
        bool rateLimited,
        DateTimeOffset now)
    {
        if (retryAtUtc < now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAtUtc),
                retryAtUtc,
                "A retry cannot be scheduled in the past.");
        }

        var attempt = GetActiveAttempt(leaseToken, deliveryAttemptId);
        attempt.Complete(
            rateLimited ? "RateLimited" : "TransientFailure",
            errorCode,
            null,
            now);
        Status = OutboxStatus.Pending;
        NextAttemptAtUtc = retryAtUtc;
        ClearLease();
    }

    public void MarkUnknown(
        string leaseToken,
        Guid deliveryAttemptId,
        string errorCode,
        DateTimeOffset now)
    {
        var attempt = GetActiveAttempt(leaseToken, deliveryAttemptId);
        attempt.Complete("Unknown", errorCode, null, now);
        Status = OutboxStatus.Unknown;
        ClearLease();
    }

    public void DeferUntil(DateTimeOffset notBeforeUtc)
    {
        if (Status != OutboxStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending notification can be deferred.");
        }

        if (notBeforeUtc > NextAttemptAtUtc)
        {
            NextAttemptAtUtc = notBeforeUtc;
        }
    }

    public void MarkPermanentFailure(
        string leaseToken,
        Guid deliveryAttemptId,
        string errorCode,
        DateTimeOffset now)
    {
        var attempt = GetActiveAttempt(leaseToken, deliveryAttemptId);
        attempt.Complete("PermanentFailure", errorCode, null, now);
        Status = OutboxStatus.PermanentFailure;
        ClearLease();
    }

    public DeliveryAttempt SuppressForDisabledDestination(
        string errorCode,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (Status != OutboxStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending notification can be suppressed for a disabled destination.");
        }

        AttemptCount = checked(AttemptCount + 1);
        var attempt = DeliveryAttempt.Start(Id, AttemptCount, now);
        attempt.Complete("SuppressedDestinationDisabled", errorCode, null, now);
        DeliveryAttempts.Add(attempt);
        Status = OutboxStatus.PermanentFailure;
        ClearLease();
        return attempt;
    }

    public void ScrubPayload()
    {
        if (Status is OutboxStatus.Pending or OutboxStatus.Leased)
        {
            throw new InvalidOperationException(
                "Only a terminal notification can have its payload scrubbed.");
        }

        PayloadJson = "{}";
    }

    public bool RecoverExpiredLease(DateTimeOffset now)
    {
        if (Status != OutboxStatus.Leased
            || LeaseExpiresAtUtc is null
            || LeaseExpiresAtUtc > now)
        {
            return false;
        }

        var attempt = DeliveryAttempts.Single(
            candidate => candidate.AttemptNumber == AttemptCount);
        if (attempt.CompletedAtUtc is null)
        {
            attempt.Complete("Unknown", "LeaseExpired", null, now);
        }

        Status = OutboxStatus.Unknown;
        ClearLease();
        return true;
    }

    private DeliveryAttempt GetActiveAttempt(
        string leaseToken,
        Guid deliveryAttemptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        if (Status != OutboxStatus.Leased
            || !string.Equals(LeaseToken, leaseToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The notification lease is not active or does not match.");
        }

        return DeliveryAttempts.Single(
            candidate => candidate.Id == deliveryAttemptId
                && candidate.AttemptNumber == AttemptCount
                && candidate.CompletedAtUtc is null);
    }

    private void ClearLease()
    {
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
    }
}
