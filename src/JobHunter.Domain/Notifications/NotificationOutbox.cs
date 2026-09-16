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
}
