using JobHunter.Domain.Common;

namespace JobHunter.Domain.Sources;

public sealed class SourceSubscription : IConcurrencyTracked
{
    private SourceSubscription()
    {
    }

    private SourceSubscription(
        Guid id,
        SourceName source,
        string subscriptionKey,
        string configurationJson,
        TimeSpan interval,
        bool enabled,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A subscription ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);

        if (interval < TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "A source interval must be at least one minute.");
        }

        Id = id;
        Source = source;
        SubscriptionKey = subscriptionKey.Trim();
        ConfigurationJson = configurationJson;
        IntervalSeconds = checked((int)interval.TotalSeconds);
        IsEnabled = enabled;
        Status = enabled ? SourceSubscriptionStatus.Enabled : SourceSubscriptionStatus.Disabled;
        NextDueAtUtc = now;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public SourceName Source { get; private set; }

    public string SubscriptionKey { get; private set; } = string.Empty;

    public string ConfigurationJson { get; private set; } = string.Empty;

    public int IntervalSeconds { get; private set; }

    public bool IsEnabled { get; private set; }

    public SourceSubscriptionStatus Status { get; private set; }

    public DateTimeOffset NextDueAtUtc { get; private set; }

    public DateTimeOffset? BackoffUntilUtc { get; private set; }

    public DateTimeOffset? LastSucceededAtUtc { get; private set; }

    public string? StatusReasonCode { get; private set; }

    public string? StatusDiagnostic { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<SourceRun> Runs { get; } = [];

    public SourceCursor? Cursor { get; private set; }

    public static SourceSubscription Create(
        SourceName source,
        string subscriptionKey,
        string configurationJson,
        TimeSpan interval,
        bool enabled,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), source, subscriptionKey, configurationJson, interval, enabled, now);

    public void SynchronizeConfiguration(
        string configurationJson,
        TimeSpan interval,
        bool enabled,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);
        if (interval < TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "A source interval must be at least one minute.");
        }

        ConfigurationJson = configurationJson;
        IntervalSeconds = checked((int)interval.TotalSeconds);

        if (!enabled)
        {
            Disable(now, "ConfigurationDisabled");
            return;
        }

        if (!IsEnabled)
        {
            Enable(now);
            return;
        }

        UpdatedAtUtc = now;
    }

    public void MarkRunning(DateTimeOffset now)
    {
        EnsureEnabled();
        Status = SourceSubscriptionStatus.Running;
        StatusReasonCode = null;
        StatusDiagnostic = null;
        UpdatedAtUtc = now;
    }

    public void MarkSucceeded(DateTimeOffset now, DateTimeOffset nextDueAtUtc)
    {
        EnsureEnabled();
        Status = SourceSubscriptionStatus.Enabled;
        LastSucceededAtUtc = now;
        NextDueAtUtc = nextDueAtUtc;
        BackoffUntilUtc = null;
        StatusReasonCode = null;
        StatusDiagnostic = null;
        UpdatedAtUtc = now;
    }

    public void MarkBackingOff(
        DateTimeOffset now,
        DateTimeOffset retryAtUtc,
        string reasonCode,
        string diagnostic)
    {
        EnsureEnabled();
        Status = SourceSubscriptionStatus.BackingOff;
        BackoffUntilUtc = retryAtUtc;
        NextDueAtUtc = retryAtUtc;
        StatusReasonCode = RequireBounded(reasonCode, 128, nameof(reasonCode));
        StatusDiagnostic = RequireBounded(diagnostic, 1024, nameof(diagnostic));
        UpdatedAtUtc = now;
    }

    public void MarkBlocked(
        DateTimeOffset now,
        DateTimeOffset? retryAtUtc,
        string reasonCode,
        string diagnostic)
    {
        EnsureEnabled();
        Status = SourceSubscriptionStatus.Blocked;
        BackoffUntilUtc = retryAtUtc;
        if (retryAtUtc is not null)
        {
            NextDueAtUtc = retryAtUtc.Value;
        }

        StatusReasonCode = RequireBounded(reasonCode, 128, nameof(reasonCode));
        StatusDiagnostic = RequireBounded(diagnostic, 1024, nameof(diagnostic));
        UpdatedAtUtc = now;
    }

    public void Disable(DateTimeOffset now, string? reasonCode = null)
    {
        IsEnabled = false;
        Status = SourceSubscriptionStatus.Disabled;
        BackoffUntilUtc = null;
        StatusReasonCode = reasonCode;
        StatusDiagnostic = null;
        UpdatedAtUtc = now;
    }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        Status = SourceSubscriptionStatus.Enabled;
        BackoffUntilUtc = null;
        StatusReasonCode = null;
        StatusDiagnostic = null;
        NextDueAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void RecoverAfterAbandonedRun(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            Status = SourceSubscriptionStatus.Disabled;
            BackoffUntilUtc = null;
            StatusReasonCode ??= "ConfigurationDisabled";
            StatusDiagnostic = null;
            UpdatedAtUtc = now;
            return;
        }

        Status = SourceSubscriptionStatus.Enabled;
        BackoffUntilUtc = null;
        StatusReasonCode = "AbandonedLeaseRecovered";
        StatusDiagnostic = "An expired source run lease was recovered and the subscription can run again.";
        NextDueAtUtc = now;
        UpdatedAtUtc = now;
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("A disabled source subscription cannot change run state.");
        }
    }

    private static string RequireBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must contain at most {maximumLength} characters.",
                parameterName);
        }

        return trimmed;
    }
}
