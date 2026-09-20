using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Evaluation;

public sealed class RuleEvaluation
{
    private RuleEvaluation()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Job Job { get; private set; } = null!;

    public Guid CandidateProfileSnapshotId { get; private set; }

    public int JobRevisionNumber { get; private set; }

    public string RubricVersion { get; private set; } = string.Empty;

    public bool PassedHardFilters { get; private set; }

    public string RuleResultsJson { get; private set; } = "[]";

    public string Explanation { get; private set; } = string.Empty;

    public DateTimeOffset EvaluatedAtUtc { get; private set; }

    public static RuleEvaluation Create(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        string rubricVersion,
        bool passedHardFilters,
        string ruleResultsJson,
        string explanation,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            CandidateProfileSnapshotId = candidateProfileSnapshotId,
            JobRevisionNumber = jobRevisionNumber,
            RubricVersion = rubricVersion,
            PassedHardFilters = passedHardFilters,
            RuleResultsJson = ruleResultsJson,
            Explanation = explanation,
            EvaluatedAtUtc = now
        };
}
