namespace JobHunter.Application.Evaluation;

public interface IRuleEvaluationStore
{
    Task<Guid> SaveAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        DeterministicEvaluation evaluation,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
