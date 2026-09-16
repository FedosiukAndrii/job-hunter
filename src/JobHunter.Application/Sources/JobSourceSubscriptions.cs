using JobHunter.Domain.Sources;

namespace JobHunter.Application.Sources;

public interface IJobSourceSubscriptionProvider
{
    IReadOnlyList<JobSourceSubscriptionDefinition> GetSubscriptions();
}

public sealed record JobSourceSubscriptionDefinition
{
    public JobSourceSubscriptionDefinition(
        SourceName source,
        string subscriptionKey,
        Uri endpoint,
        string? queryId,
        int maximumItems,
        TimeSpan interval,
        bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "A source subscription endpoint must be absolute.",
                nameof(endpoint));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        if (interval < TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "A source interval must be at least one minute.");
        }

        Source = source;
        SubscriptionKey = subscriptionKey.Trim();
        Endpoint = endpoint;
        QueryId = string.IsNullOrWhiteSpace(queryId) ? null : queryId.Trim();
        MaximumItems = maximumItems;
        Interval = interval;
        Enabled = enabled;
    }

    public SourceName Source { get; }

    public string SubscriptionKey { get; }

    public Uri Endpoint { get; }

    public string? QueryId { get; }

    public int MaximumItems { get; }

    public TimeSpan Interval { get; }

    public bool Enabled { get; }
}

public sealed record DueJobSourceSubscription(
    Guid Id,
    SourceName Source,
    string SubscriptionKey,
    Uri Endpoint,
    string? QueryId,
    SourceCursorValue? Cursor,
    int MaximumItems,
    TimeSpan Interval);
