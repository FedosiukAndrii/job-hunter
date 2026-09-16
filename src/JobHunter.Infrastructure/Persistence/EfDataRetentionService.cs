using JobHunter.Application.Persistence;
using JobHunter.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfDataRetentionService(
    IDbContextFactory<JobHunterDbContext> contextFactory)
    : IDataRetentionService
{
    private const string RetainedOutboxPayload = "{}";
    private const int BatchSize = 500;

    public async Task<DataRetentionSummary> CleanupAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var sourceRunIds = (await context.SourceRuns
                .AsNoTracking()
                .Where(run => run.CompletedAtUtc != null)
                .Select(run => new { run.Id, run.CompletedAtUtc })
                .ToListAsync(cancellationToken))
            .Where(run => run.CompletedAtUtc < cutoffUtc)
            .Select(run => run.Id)
            .ToArray();
        var attemptIds = (await context.DeliveryAttempts
                .AsNoTracking()
                .Where(attempt => attempt.CompletedAtUtc != null)
                .Select(attempt => new { attempt.Id, attempt.CompletedAtUtc })
                .ToListAsync(cancellationToken))
            .Where(attempt => attempt.CompletedAtUtc < cutoffUtc)
            .Select(attempt => attempt.Id)
            .ToArray();
        var outboxIds = (await context.NotificationOutbox
                .AsNoTracking()
                .Where(outbox =>
                    outbox.PayloadJson != RetainedOutboxPayload
                    && (outbox.Status == OutboxStatus.Sent
                        || outbox.Status == OutboxStatus.Unknown
                        || outbox.Status == OutboxStatus.PermanentFailure))
                .Select(outbox => new { outbox.Id, outbox.CreatedAtUtc })
                .ToListAsync(cancellationToken))
            .Where(outbox => outbox.CreatedAtUtc < cutoffUtc)
            .Select(outbox => outbox.Id)
            .ToArray();
        var applicationEventIds = (await context.ApplicationEvents
                .AsNoTracking()
                .Select(applicationEvent => new
                {
                    applicationEvent.Id,
                    applicationEvent.CreatedAtUtc
                })
                .ToListAsync(cancellationToken))
            .Where(applicationEvent => applicationEvent.CreatedAtUtc < cutoffUtc)
            .Select(applicationEvent => applicationEvent.Id)
            .ToArray();

        var observationCount = 0;
        var sourceRunCount = 0;
        foreach (var batch in sourceRunIds.Chunk(BatchSize))
        {
            observationCount += await context.JobObservations.CountAsync(
                observation => batch.Contains(observation.SourceRunId),
                cancellationToken);
            sourceRunCount += await context.SourceRuns
                .Where(run => batch.Contains(run.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var deliveryAttemptCount = 0;
        foreach (var batch in attemptIds.Chunk(BatchSize))
        {
            deliveryAttemptCount += await context.DeliveryAttempts
                .Where(attempt => batch.Contains(attempt.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var scrubbedOutboxCount = 0;
        foreach (var batch in outboxIds.Chunk(BatchSize))
        {
            scrubbedOutboxCount += await context.NotificationOutbox
                .Where(outbox => batch.Contains(outbox.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        outbox => outbox.PayloadJson,
                        RetainedOutboxPayload),
                    cancellationToken);
        }

        var applicationEventCount = 0;
        foreach (var batch in applicationEventIds.Chunk(BatchSize))
        {
            applicationEventCount += await context.ApplicationEvents
                .Where(applicationEvent => batch.Contains(applicationEvent.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return new DataRetentionSummary(
            sourceRunCount,
            observationCount,
            deliveryAttemptCount,
            scrubbedOutboxCount,
            applicationEventCount);
    }
}
