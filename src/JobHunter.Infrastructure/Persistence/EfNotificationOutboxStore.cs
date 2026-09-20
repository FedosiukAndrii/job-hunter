using System.Data;
using System.Text.Json;
using JobHunter.Application.Notifications;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfNotificationOutboxStore(
    IDbContextFactory<JobHunterDbContext> contextFactory)
    : INotificationOutboxStore, INotificationDestinationStateStore
{
    private const int MaximumActivePerDestination = 1_000;
    private const int MaximumActiveGlobally = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public async Task<NotificationEnqueueOutcome> EnqueueAsync(
        NotificationIntent intent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var destination = await context.NotificationDestinationStates.FindAsync(
            [intent.DestinationId],
            cancellationToken);
        if (destination is { IsEnabled: false })
        {
            return NotificationEnqueueOutcome.DestinationDisabled;
        }

        if (await ExistsAsync(context, intent, cancellationToken))
        {
            return NotificationEnqueueOutcome.AlreadyExists;
        }

        if (intent.SuppressPossibleDuplicateNotifications
            && await HasSentPossibleDuplicateAsync(
                context,
                intent.DestinationId,
                intent.JobId,
                cancellationToken))
        {
            return NotificationEnqueueOutcome.PossibleDuplicateAlreadySent;
        }

        var activeCount = await context.NotificationOutbox.CountAsync(
            candidate => candidate.Status == OutboxStatus.Pending
                || candidate.Status == OutboxStatus.Leased,
            cancellationToken);
        if (activeCount >= MaximumActiveGlobally)
        {
            return NotificationEnqueueOutcome.QueueFull;
        }

        var destinationActiveCount = await context.NotificationOutbox.CountAsync(
            candidate => candidate.DestinationId == intent.DestinationId
                && (candidate.Status == OutboxStatus.Pending
                    || candidate.Status == OutboxStatus.Leased),
            cancellationToken);
        if (destinationActiveCount >= MaximumActivePerDestination)
        {
            return NotificationEnqueueOutcome.QueueFull;
        }

        if (destination is null)
        {
            destination = NotificationDestinationState.CreateEnabled(
                intent.DestinationId,
                now);
            context.NotificationDestinationStates.Add(destination);
        }

        var outbox = NotificationOutbox.Create(
            intent.DestinationId,
            intent.JobId,
            intent.NotificationVersion,
            JsonSerializer.Serialize(intent.Payload, JsonOptions),
            now,
            intent.SuppressPossibleDuplicateNotifications);
        if (destination.RateLimitedUntilUtc is { } rateLimitedUntilUtc)
        {
            outbox.DeferUntil(rateLimitedUntilUtc);
        }

        context.NotificationOutbox.Add(outbox);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return NotificationEnqueueOutcome.Created;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            await using var verificationContext =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await ExistsAsync(verificationContext, intent, cancellationToken))
            {
                return NotificationEnqueueOutcome.AlreadyExists;
            }

            throw;
        }
    }

    public async Task<int> RecoverExpiredLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var leased = await context.NotificationOutbox
            .Include(outbox => outbox.DeliveryAttempts)
            .Where(outbox => outbox.Status == OutboxStatus.Leased)
            .ToListAsync(cancellationToken);

        var recovered = leased.Count(outbox => outbox.RecoverExpiredLease(now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return recovered;
    }

    public async Task<NotificationOutboxLease?> TryLeaseNextAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var destinationStates = await context.NotificationDestinationStates
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var unavailableDestinations = destinationStates
            .Where(destination => !destination.IsEnabled
                || destination.RateLimitedUntilUtc > now)
            .Select(destination => destination.DestinationId)
            .ToHashSet(StringComparer.Ordinal);
        var pending = await context.NotificationOutbox
            .Include(outbox => outbox.DeliveryAttempts)
            .Where(outbox => outbox.Status == OutboxStatus.Pending)
            .ToListAsync(cancellationToken);
        var candidates = pending
            .Where(candidate => candidate.NextAttemptAtUtc <= now
                && !unavailableDestinations.Contains(candidate.DestinationId))
            .OrderBy(candidate => candidate.NextAttemptAtUtc)
            .ThenBy(candidate => candidate.CreatedAtUtc)
            .ToArray();
        foreach (var outbox in candidates)
        {
            if (outbox.SuppressPossibleDuplicateNotifications
                && await HasSentPossibleDuplicateAsync(
                    context,
                    outbox.DestinationId,
                    outbox.JobId,
                    cancellationToken))
            {
                var suppressionAttempt = outbox.SuppressForPossibleDuplicate(
                    "PossibleDuplicateAlreadySent",
                    now);
                context.DeliveryAttempts.Add(suppressionAttempt);
                continue;
            }

            JobNotification payload;
            try
            {
                payload = JsonSerializer.Deserialize<JobNotification>(
                    outbox.PayloadJson,
                    JsonOptions)
                    ?? throw new JsonException("The outbox payload is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Notification outbox item '{outbox.Id}' has an invalid payload.",
                    exception);
            }

            var attempt = outbox.Lease(now, leaseDuration);
            context.DeliveryAttempts.Add(attempt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new NotificationOutboxLease(
                outbox.Id,
                outbox.DestinationId,
                outbox.LeaseToken!,
                attempt.Id,
                attempt.AttemptNumber,
                payload);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task CompleteAsync(
        NotificationOutboxLease lease,
        NotificationSendResult result,
        DateTimeOffset? destinationRateLimitedUntilUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(result);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var outbox = await context.NotificationOutbox
            .Include(candidate => candidate.DeliveryAttempts)
            .SingleAsync(candidate => candidate.Id == lease.OutboxId, cancellationToken);

        switch (result.Outcome)
        {
            case NotificationSendOutcome.Sent:
                outbox.MarkSent(
                    lease.LeaseToken,
                    lease.DeliveryAttemptId,
                    Require(result.ExternalMessageId, nameof(result.ExternalMessageId)),
                    now);
                break;
            case NotificationSendOutcome.TransientFailure:
            case NotificationSendOutcome.RateLimited:
                var retryAtUtc = result.RetryAtUtc
                    ?? throw new InvalidOperationException(
                        "A retry result must include RetryAtUtc.");
                outbox.Requeue(
                    lease.LeaseToken,
                    lease.DeliveryAttemptId,
                    retryAtUtc,
                    Require(result.ErrorCode, nameof(result.ErrorCode)),
                    result.Outcome == NotificationSendOutcome.RateLimited,
                    now);
                break;
            case NotificationSendOutcome.Unknown:
                outbox.MarkUnknown(
                    lease.LeaseToken,
                    lease.DeliveryAttemptId,
                    Require(result.ErrorCode, nameof(result.ErrorCode)),
                    now);
                break;
            case NotificationSendOutcome.PermanentFailure:
                var failureCode = Require(
                    result.ErrorCode,
                    nameof(result.ErrorCode));
                outbox.MarkPermanentFailure(
                    lease.LeaseToken,
                    lease.DeliveryAttemptId,
                    failureCode,
                    now);
                if (result.DisableDestination)
                {
                    var destination =
                        await context.NotificationDestinationStates.FindAsync(
                            [lease.DestinationId],
                            cancellationToken);
                    if (destination is null)
                    {
                        destination = NotificationDestinationState.CreateEnabled(
                            lease.DestinationId,
                            now);
                        context.NotificationDestinationStates.Add(destination);
                    }

                    destination.Disable(
                        failureCode,
                        now);
                    outbox.ScrubPayload();

                    var pendingForDestination = await context.NotificationOutbox
                        .Include(candidate => candidate.DeliveryAttempts)
                        .Where(candidate =>
                            candidate.Id != outbox.Id
                            && candidate.DestinationId == lease.DestinationId
                            && candidate.Status == OutboxStatus.Pending)
                        .ToListAsync(cancellationToken);
                    foreach (var pending in pendingForDestination)
                    {
                        var suppressionAttempt =
                            pending.SuppressForDisabledDestination(failureCode, now);
                        pending.ScrubPayload();
                        context.DeliveryAttempts.Add(suppressionAttempt);
                    }
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported notification outcome '{result.Outcome}'.");
        }

        if (result.Outcome == NotificationSendOutcome.RateLimited
            && destinationRateLimitedUntilUtc is null)
        {
            throw new InvalidOperationException(
                "A rate-limited result must include a destination cooldown.");
        }

        if (destinationRateLimitedUntilUtc is { } rateLimitedUntilUtc)
        {
            var destination =
                await context.NotificationDestinationStates.FindAsync(
                    [lease.DestinationId],
                    cancellationToken);
            if (destination is null)
            {
                destination = NotificationDestinationState.CreateEnabled(
                    lease.DestinationId,
                    now);
                context.NotificationDestinationStates.Add(destination);
            }

            destination.DeferUntil(rateLimitedUntilUtc, now);
            if (outbox.Status == OutboxStatus.Pending)
            {
                outbox.DeferUntil(rateLimitedUntilUtc);
            }

            var pendingForDestination = await context.NotificationOutbox
                .Where(candidate =>
                    candidate.Id != outbox.Id
                    && candidate.DestinationId == lease.DestinationId
                    && candidate.Status == OutboxStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var pending in pendingForDestination)
            {
                pending.DeferUntil(rateLimitedUntilUtc);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDestinationEnabledAsync(
        string destinationId,
        bool enabled,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedDestinationId =
            new NotificationDestination(destinationId).Id;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var destination = await context.NotificationDestinationStates.FindAsync(
            [normalizedDestinationId],
            cancellationToken);
        if (destination is null)
        {
            destination = NotificationDestinationState.CreateEnabled(
                normalizedDestinationId,
                now);
            context.NotificationDestinationStates.Add(destination);
        }

        if (enabled)
        {
            destination.Enable(now);
        }
        else
        {
            destination.Disable(
                Require(failureCode, nameof(failureCode)),
                now);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationDestinationStateSnapshot?> GetAsync(
        string destinationId,
        CancellationToken cancellationToken)
    {
        var normalizedDestinationId =
            new NotificationDestination(destinationId).Id;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var destination = await context.NotificationDestinationStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.DestinationId == normalizedDestinationId,
                cancellationToken);
        return destination is null
            ? null
            : new NotificationDestinationStateSnapshot(
                destination.DestinationId,
                destination.IsEnabled,
                destination.FailureCode,
                destination.UpdatedAtUtc);
    }

    private static Task<bool> ExistsAsync(
        JobHunterDbContext context,
        NotificationIntent intent,
        CancellationToken cancellationToken) =>
        context.NotificationOutbox.AnyAsync(
            candidate => candidate.DestinationId == intent.DestinationId
                && candidate.JobId == intent.JobId
                && candidate.NotificationVersion == intent.NotificationVersion,
            cancellationToken);

    private static async Task<bool> HasSentPossibleDuplicateAsync(
        JobHunterDbContext context,
        string destinationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var relatedJobIds = await context.JobPossibleDuplicates
            .AsNoTracking()
            .Where(pair =>
                pair.MatchReason == CrossSourceJobDuplicateMatcher.CompanyTitlePublishedAtV2
                && (pair.FirstJobId == jobId || pair.SecondJobId == jobId))
            .Select(pair => pair.FirstJobId == jobId ? pair.SecondJobId : pair.FirstJobId)
            .ToListAsync(cancellationToken);
        return relatedJobIds.Count > 0
            && await context.NotificationOutbox.AnyAsync(
                candidate => candidate.DestinationId == destinationId
                    && candidate.Status == OutboxStatus.Sent
                    && relatedJobIds.Contains(candidate.JobId),
                cancellationToken);
    }

    private static string Require(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
