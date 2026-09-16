namespace JobHunter.Application.Persistence;

public interface IDataRetentionService
{
    Task<DataRetentionSummary> CleanupAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);
}

public sealed record DataRetentionSummary(
    int SourceRunCount,
    int ObservationCount,
    int DeliveryAttemptCount,
    int ScrubbedOutboxCount,
    int ApplicationEventCount);
