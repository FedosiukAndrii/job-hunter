using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;

namespace JobHunter.Application.Persistence;

public interface IJobIngestionStore
{
    Task<JobIngestionResult> PersistAsync(
        Guid sourceRunId,
        Guid sourceSubscriptionId,
        string? queryId,
        IReadOnlyCollection<JobSourceRecord> records,
        CancellationToken cancellationToken);
}

public sealed record JobIngestionResult(
    int ObservedCount,
    int CreatedCount,
    int UpdatedCount,
    int DuplicateCount);

public interface ISourceRunStore
{
    Task<SourceRunLease?> TryStartAsync(
        Guid sourceSubscriptionId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        Guid sourceRunId,
        string leaseToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid sourceRunId,
        string leaseToken,
        JobSourceResult sourceResult,
        JobIngestionResult ingestionResult,
        DateTimeOffset now,
        TimeSpan defaultInterval,
        CancellationToken cancellationToken);

    Task<int> RecoverExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record SourceRunLease(Guid SourceRunId, string LeaseToken);

public interface IDatabaseMaintenance
{
    Task BackupAsync(string outputPath, CancellationToken cancellationToken);

    Task RestoreAsync(
        string backupPath,
        string destinationPath,
        CancellationToken cancellationToken);

    Task<string> CheckIntegrityAsync(
        string databasePath,
        CancellationToken cancellationToken);
}
