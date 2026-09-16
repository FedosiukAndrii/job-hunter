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

    public int Score { get; private set; }

    public int RulesOnlyThreshold { get; private set; }

    public int RulesAndAiThreshold { get; private set; }

    public string RuleResultsJson { get; private set; } = "[]";

    public string EvidenceJson { get; private set; } = "[]";

    public string MissingDataJson { get; private set; } = "[]";

    public string Explanation { get; private set; } = string.Empty;

    public DateTimeOffset EvaluatedAtUtc { get; private set; }

    public static RuleEvaluation Create(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        string rubricVersion,
        bool passedHardFilters,
        JobScore score,
        int rulesOnlyThreshold,
        int rulesAndAiThreshold,
        string ruleResultsJson,
        string evidenceJson,
        string missingDataJson,
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
            Score = score.Value,
            RulesOnlyThreshold = rulesOnlyThreshold,
            RulesAndAiThreshold = rulesAndAiThreshold,
            RuleResultsJson = ruleResultsJson,
            EvidenceJson = evidenceJson,
            MissingDataJson = missingDataJson,
            Explanation = explanation,
            EvaluatedAtUtc = now
        };
}
