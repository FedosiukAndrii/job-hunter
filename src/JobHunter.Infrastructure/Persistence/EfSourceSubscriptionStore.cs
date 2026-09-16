using System.Data;
using System.Text.Json;
using JobHunter.Application.Persistence;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfSourceSubscriptionStore(IDbContextFactory<JobHunterDbContext> contextFactory): ISourceSubscriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public async Task SynchronizeAsync( IReadOnlyCollection<JobSourceSubscriptionDefinition> definitions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var duplicateDefinition = definitions
            .GroupBy( definition => (definition.Source, definition.SubscriptionKey), SourceSubscriptionKeyComparer.Instance)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateDefinition is not null)
        {
            throw new InvalidOperationException(
                $"Source subscription '{duplicateDefinition.Key.Source}/"
                + $"{duplicateDefinition.Key.SubscriptionKey}' is registered more than once.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync( IsolationLevel.Serializable, cancellationToken);
        var existingSubscriptions = await context.SourceSubscriptions.ToListAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            var configurationJson = JsonSerializer.Serialize(new StoredSourceSubscriptionConfiguration( definition.Endpoint.AbsoluteUri, definition.QueryId, definition.MaximumItems),JsonOptions);
            var subscription = existingSubscriptions.SingleOrDefault( candidate => candidate.Source == definition.Source && string.Equals(candidate.SubscriptionKey, definition.SubscriptionKey, StringComparison.Ordinal));
            
            if (subscription is null)
            {
                subscription = SourceSubscription.Create( definition.Source, definition.SubscriptionKey, configurationJson, definition.Interval, definition.Enabled, now);
                context.SourceSubscriptions.Add(subscription);
                existingSubscriptions.Add(subscription);
            }
            else
                subscription.SynchronizeConfiguration( configurationJson, definition.Interval, definition.Enabled, now);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DueJobSourceSubscription>> GetDueAsync( DateTimeOffset now, int maximumCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var enabledSubscriptions = await context.SourceSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Cursor)
            .Where(subscription => subscription.IsEnabled)
            .ToListAsync(cancellationToken);

        return enabledSubscriptions
            .Where(subscription => IsDue(subscription, now))
            .OrderBy(subscription => subscription.NextDueAtUtc)
            .ThenBy(subscription => subscription.Source.Value, StringComparer.Ordinal)
            .ThenBy(subscription => subscription.SubscriptionKey, StringComparer.Ordinal)
            .Take(maximumCount)
            .Select(ToDueSubscription)
            .ToArray();
    }

    public async Task<IReadOnlyList<DueJobSourceSubscription>> GetRunnableAsync(
        SourceName source,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subscriptions = await context.SourceSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Cursor)
            .Where(subscription =>
                subscription.IsEnabled
                && subscription.Source == source
                && (subscription.Status == SourceSubscriptionStatus.Enabled
                    || subscription.Status == SourceSubscriptionStatus.BackingOff))
            .OrderBy(subscription => subscription.SubscriptionKey)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

        return subscriptions.Select(ToDueSubscription).ToArray();
    }

    public async Task<IReadOnlyList<PersistedSourceSubscriptionState>> GetStatesAsync(
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subscriptions = await context.SourceSubscriptions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return subscriptions
            .Select(sub => new PersistedSourceSubscriptionState( sub.Source, sub.SubscriptionKey, sub.IsEnabled, sub.Status, sub.StatusReasonCode, sub.StatusDiagnostic))
            .ToArray();
    }

    private static bool IsDue( SourceSubscription subscription, DateTimeOffset now) =>
        subscription.NextDueAtUtc <= now
        && (subscription.Status == SourceSubscriptionStatus.Enabled || subscription.Status == SourceSubscriptionStatus.BackingOff && subscription.BackoffUntilUtc <= now);

    private static DueJobSourceSubscription ToDueSubscription(SourceSubscription subscription)
    {
        StoredSourceSubscriptionConfiguration configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<StoredSourceSubscriptionConfiguration> ( subscription.ConfigurationJson, JsonOptions) ?? throw new JsonException("The subscription configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException( $"Source subscription '{subscription.Id}' has invalid persisted configuration.", exception);
        }

        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidDataException(
                $"Source subscription '{subscription.Id}' has an invalid endpoint.");
        }

        return new DueJobSourceSubscription(
            subscription.Id,
            subscription.Source,
            subscription.SubscriptionKey,
            endpoint,
            configuration.QueryId,
            subscription.Cursor is null
                ? null
                : new SourceCursorValue(
                    subscription.Cursor.EntityTag,
                    subscription.Cursor.LastModifiedAtUtc,
                    subscription.Cursor.OpaqueValue),
            configuration.MaximumItems,
            TimeSpan.FromSeconds(subscription.IntervalSeconds));
    }

    private sealed record StoredSourceSubscriptionConfiguration(
        string Endpoint,
        string? QueryId,
        int MaximumItems);

    private sealed class SourceSubscriptionKeyComparer
        : IEqualityComparer<(SourceName Source, string SubscriptionKey)>
    {
        public static SourceSubscriptionKeyComparer Instance { get; } = new();

        public bool Equals(
            (SourceName Source, string SubscriptionKey) x,
            (SourceName Source, string SubscriptionKey) y) =>
            x.Source == y.Source
            && string.Equals(x.SubscriptionKey, y.SubscriptionKey, StringComparison.Ordinal);

        public int GetHashCode((SourceName Source, string SubscriptionKey) obj) =>
            HashCode.Combine(
                obj.Source,
                StringComparer.Ordinal.GetHashCode(obj.SubscriptionKey));
    }
}
