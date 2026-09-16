using JobHunter.Domain.Common;

namespace JobHunter.Domain.Notifications;

public sealed class NotificationDestinationState : IConcurrencyTracked
{
    private NotificationDestinationState()
    {
    }

    public string DestinationId { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? DisabledAtUtc { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset? RateLimitedUntilUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public long ConcurrencyVersion { get; set; }

    public static NotificationDestinationState CreateEnabled(
        string destinationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationId);
        var normalized = destinationId.Trim();
        if (normalized.Length > 256)
        {
            throw new ArgumentException(
                "A notification destination ID must contain at most 256 characters.",
                nameof(destinationId));
        }

        return new NotificationDestinationState
        {
            DestinationId = normalized,
            IsEnabled = true,
            UpdatedAtUtc = now
        };
    }

    public void Disable(string failureCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        IsEnabled = false;
        DisabledAtUtc = now;
        FailureCode = failureCode.Trim().Length <= 128
            ? failureCode.Trim()
            : failureCode.Trim()[..128];
        RateLimitedUntilUtc = null;
        UpdatedAtUtc = now;
    }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        DisabledAtUtc = null;
        FailureCode = null;
        RateLimitedUntilUtc = null;
        UpdatedAtUtc = now;
    }

    public void DeferUntil(DateTimeOffset retryAtUtc, DateTimeOffset now)
    {
        if (retryAtUtc < now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAtUtc),
                retryAtUtc,
                "A destination rate limit cannot end in the past.");
        }

        if (RateLimitedUntilUtc is null || retryAtUtc > RateLimitedUntilUtc)
        {
            RateLimitedUntilUtc = retryAtUtc;
            UpdatedAtUtc = now;
        }
    }
}
