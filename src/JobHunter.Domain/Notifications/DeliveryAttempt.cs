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

    public static DeliveryAttempt Start(
        Guid notificationOutboxId,
        int attemptNumber,
        DateTimeOffset now)
    {
        if (notificationOutboxId == Guid.Empty)
        {
            throw new ArgumentException(
                "A notification outbox ID is required.",
                nameof(notificationOutboxId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);

        return new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            NotificationOutboxId = notificationOutboxId,
            AttemptNumber = attemptNumber,
            Outcome = "Started",
            StartedAtUtc = now
        };
    }

    public void Complete(
        string outcome,
        string? errorCode,
        string? externalMessageId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        if (CompletedAtUtc is not null)
        {
            throw new InvalidOperationException("The delivery attempt is already complete.");
        }

        Outcome = Bound(outcome, 64)!;
        ErrorCode = Bound(errorCode, 128);
        ExternalMessageId = Bound(externalMessageId, 256);
        CompletedAtUtc = now;
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
