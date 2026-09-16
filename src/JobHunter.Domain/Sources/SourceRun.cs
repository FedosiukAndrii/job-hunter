using JobHunter.Domain.Common;

namespace JobHunter.Domain.Sources;

public sealed class SourceRun : IConcurrencyTracked
{
    private SourceRun()
    {
    }

    private SourceRun(
        Guid id,
        Guid sourceSubscriptionId,
        string leaseToken,
        DateTimeOffset startedAtUtc,
        DateTimeOffset leaseExpiresAtUtc)
    {
        Id = id;
        SourceSubscriptionId = sourceSubscriptionId;
        LeaseToken = leaseToken;
        Status = SourceRunStatus.Running;
        StartedAtUtc = startedAtUtc;
        HeartbeatAtUtc = startedAtUtc;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid SourceSubscriptionId { get; private set; }

    public SourceSubscription SourceSubscription { get; private set; } = null!;

    public SourceRunStatus Status { get; private set; }

    public string LeaseToken { get; private set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset HeartbeatAtUtc { get; private set; }

    public DateTimeOffset LeaseExpiresAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public int ObservedCount { get; private set; }

    public int CreatedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int DuplicateCount { get; private set; }

    public DateTimeOffset? RetryAfterUtc { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? Diagnostic { get; private set; }

    public string? CursorJson { get; private set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<JobHunter.Domain.Jobs.JobObservation> Observations { get; } = [];

    public static SourceRun Start(
        Guid sourceSubscriptionId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        if (sourceSubscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A source subscription ID is required.", nameof(sourceSubscriptionId));
        }

        EnsureLeaseDuration(leaseDuration);
        return new SourceRun(
            Guid.NewGuid(),
            sourceSubscriptionId,
            Guid.NewGuid().ToString("N"),
            now,
            now.Add(leaseDuration));
    }

    public void Heartbeat(
        string leaseToken,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        EnsureRunningLease(leaseToken, now);
        EnsureLeaseDuration(leaseDuration);
        HeartbeatAtUtc = now;
        LeaseExpiresAtUtc = now.Add(leaseDuration);
    }

    public void Complete(
        string leaseToken,
        SourceRunStatus status,
        DateTimeOffset now,
        int observedCount,
        int createdCount,
        int updatedCount,
        int duplicateCount,
        DateTimeOffset? retryAfterUtc,
        string? errorCode,
        string? diagnostic,
        string? cursorJson)
    {
        if (status == SourceRunStatus.Running)
        {
            throw new ArgumentException("A completed source run cannot remain Running.", nameof(status));
        }

        EnsureRunningLease(leaseToken, now);
        if (observedCount < 0 || createdCount < 0 || updatedCount < 0 || duplicateCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedCount),
                "Source run counts cannot be negative.");
        }

        Status = status;
        CompletedAtUtc = now;
        ObservedCount = observedCount;
        CreatedCount = createdCount;
        UpdatedCount = updatedCount;
        DuplicateCount = duplicateCount;
        RetryAfterUtc = retryAfterUtc;
        ErrorCode = Bound(errorCode, 128);
        Diagnostic = Bound(diagnostic, 1024);
        CursorJson = Bound(cursorJson, 8192);
        LeaseToken = string.Empty;
        LeaseExpiresAtUtc = now;
    }

    public bool RecoverIfExpired(DateTimeOffset now)
    {
        if (Status != SourceRunStatus.Running || LeaseExpiresAtUtc > now)
        {
            return false;
        }

        Status = SourceRunStatus.Failed;
        CompletedAtUtc = now;
        ErrorCode = "AbandonedLease";
        Diagnostic = "The source run lease expired before completion and was recovered.";
        LeaseToken = string.Empty;
        return true;
    }

    public void Abandon(
        string leaseToken,
        DateTimeOffset now,
        string errorCode,
        string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        if (Status != SourceRunStatus.Running
            || !string.Equals(LeaseToken, leaseToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source run lease is not active or does not match.");
        }

        Status = SourceRunStatus.Failed;
        CompletedAtUtc = now;
        ErrorCode = Bound(errorCode, 128);
        Diagnostic = Bound(diagnostic, 1024);
        LeaseToken = string.Empty;
        LeaseExpiresAtUtc = now;
    }

    private void EnsureRunningLease(string leaseToken, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        if (Status != SourceRunStatus.Running
            || !string.Equals(LeaseToken, leaseToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The source run lease is not active or does not match.");
        }

        if (LeaseExpiresAtUtc <= now)
        {
            throw new InvalidOperationException("The source run lease has expired.");
        }
    }

    private static void EnsureLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration < TimeSpan.FromSeconds(5)
            || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                leaseDuration,
                "A source run lease must be between 5 seconds and 1 hour.");
        }
    }

    private static string? Bound(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
