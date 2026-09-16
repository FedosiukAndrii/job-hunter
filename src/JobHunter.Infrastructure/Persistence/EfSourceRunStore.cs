using System.Data;
using JobHunter.Application.Persistence;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfSourceRunStore(IDbContextFactory<JobHunterDbContext> contextFactory)
    : ISourceRunStore
{
    public async Task<SourceRunLease?> TryStartAsync(
        Guid sourceSubscriptionId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var subscription = await context.SourceSubscriptions.SingleOrDefaultAsync(
            candidate => candidate.Id == sourceSubscriptionId,
            cancellationToken);
        if (subscription is null)
        {
            throw new InvalidOperationException(
                $"Source subscription '{sourceSubscriptionId}' does not exist.");
        }

        if (!subscription.IsEnabled
            || subscription.Status is SourceSubscriptionStatus.Disabled
                or SourceSubscriptionStatus.Blocked
            || (subscription.Status == SourceSubscriptionStatus.BackingOff
                && subscription.BackoffUntilUtc > now))
        {
            return null;
        }

        var activeRun = await context.SourceRuns.SingleOrDefaultAsync(
            run => run.SourceSubscriptionId == sourceSubscriptionId
                && run.Status == SourceRunStatus.Running,
            cancellationToken);
        if (activeRun is not null)
        {
            if (!activeRun.RecoverIfExpired(now))
            {
                return null;
            }

            subscription.RecoverAfterAbandonedRun(now);
        }

        var run = SourceRun.Start(sourceSubscriptionId, now, leaseDuration);
        subscription.MarkRunning(now);
        context.SourceRuns.Add(run);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SourceRunLease(run.Id, run.LeaseToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task HeartbeatAsync(
        Guid sourceRunId,
        string leaseToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var run = await context.SourceRuns.SingleOrDefaultAsync(
            candidate => candidate.Id == sourceRunId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Source run '{sourceRunId}' does not exist.");

        run.Heartbeat(leaseToken, now, leaseDuration);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid sourceRunId,
        string leaseToken,
        JobSourceResult sourceResult,
        JobIngestionResult ingestionResult,
        DateTimeOffset now,
        TimeSpan defaultInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceResult);
        ArgumentNullException.ThrowIfNull(ingestionResult);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var run = await context.SourceRuns.SingleOrDefaultAsync(
            candidate => candidate.Id == sourceRunId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Source run '{sourceRunId}' does not exist.");
        var subscription = await context.SourceSubscriptions.SingleAsync(
            candidate => candidate.Id == run.SourceSubscriptionId,
            cancellationToken);

        run.Complete(
            leaseToken,
            sourceResult.Status,
            now,
            ingestionResult.ObservedCount,
            ingestionResult.CreatedCount,
            ingestionResult.UpdatedCount,
            ingestionResult.DuplicateCount,
            sourceResult.RetryAfterUtc,
            sourceResult.ErrorCode,
            sourceResult.Diagnostic,
            sourceResult.Cursor is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(sourceResult.Cursor));

        switch (sourceResult.Status)
        {
            case SourceRunStatus.Succeeded:
            case SourceRunStatus.Partial:
                var nextDueAtUtc = Max(
                    Max(
                        now.Add(defaultInterval),
                        sourceResult.NextPollNotBeforeUtc),
                    sourceResult.Status == SourceRunStatus.Partial
                        ? sourceResult.RetryAfterUtc
                        : null);
                subscription.MarkSucceeded(now, nextDueAtUtc);
                break;
            case SourceRunStatus.Blocked:
                subscription.MarkBlocked(
                    now,
                    sourceResult.RetryAfterUtc,
                    sourceResult.ErrorCode ?? "Blocked",
                    sourceResult.Diagnostic ?? "The source reported a blocked state.");
                break;
            case SourceRunStatus.Failed:
                subscription.MarkBackingOff(
                    now,
                    sourceResult.RetryAfterUtc ?? now.Add(defaultInterval),
                    sourceResult.ErrorCode ?? "SourceFailed",
                    sourceResult.Diagnostic ?? "The source failed without a diagnostic.");
                break;
            default:
                throw new InvalidOperationException(
                    $"Source result status '{sourceResult.Status}' cannot complete a run.");
        }

        if (sourceResult.Cursor is not null)
        {
            var cursor = await context.SourceCursors.SingleOrDefaultAsync(
                candidate => candidate.SourceSubscriptionId == subscription.Id,
                cancellationToken);
            if (cursor is null)
            {
                context.SourceCursors.Add(
                    SourceCursor.Create(
                        subscription.Id,
                        sourceResult.Cursor.EntityTag,
                        sourceResult.Cursor.LastModifiedAtUtc,
                        sourceResult.Cursor.OpaqueValue,
                        now));
            }
            else
            {
                cursor.Update(
                    sourceResult.Cursor.EntityTag,
                    sourceResult.Cursor.LastModifiedAtUtc,
                    sourceResult.Cursor.OpaqueValue,
                    now);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> RecoverExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var runningRuns = await context.SourceRuns
            .Include(run => run.SourceSubscription)
            .Where(run => run.Status == SourceRunStatus.Running)
            .ToListAsync(cancellationToken);
        var expiredRuns = runningRuns.Where(run => run.LeaseExpiresAtUtc <= now);

        var recoveredCount = 0;
        foreach (var run in expiredRuns)
        {
            if (run.RecoverIfExpired(now))
            {
                run.SourceSubscription.RecoverAfterAbandonedRun(now);
                recoveredCount++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return recoveredCount;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset? second) =>
        second is not null && second.Value > first ? second.Value : first;
}
