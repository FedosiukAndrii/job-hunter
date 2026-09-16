using JobHunter.AI.Abstractions;

namespace JobHunter.Application.Evaluation;

public interface IAiAnalysisStore
{
    Task<StoredJobAnalysis?> GetAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        string provider,
        string schemaVersion,
        string rubricVersion,
        CancellationToken cancellationToken);

    Task SaveAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        JobAnalysisResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record StoredJobAnalysis(
    JobAnalysisResult Result,
    DateTimeOffset RecordedAtUtc);
