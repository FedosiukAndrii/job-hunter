namespace JobHunter.Domain.Notifications;

public sealed class DeliveryAttempt
{
    private DeliveryAttempt()
    {
    }

    public Guid Id { get; private set; }

    public Guid NotificationOutboxId { get; private set; }

    public NotificationOutbox NotificationOutbox { get; private set; } = null!;

    public int AttemptNumber { get; private set; }

    public string Outcome { get; private set; } = string.Empty;

    public string? ErrorCode { get; private set; }

    public string? ExternalMessageId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }
}
