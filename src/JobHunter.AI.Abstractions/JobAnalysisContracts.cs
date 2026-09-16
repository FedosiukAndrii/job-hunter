using System.ComponentModel;
using System.Text.Json.Serialization;

namespace JobHunter.AI.Abstractions;

public static class JobAnalysisSchema
{
    public const string Version = "job-analysis-v1";
}

public interface IJobAnalyzer
{
    JobAnalyzerCapabilities Capabilities { get; }

    Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken);

    Task<JobAnalysisResult> AnalyzeAsync(
        JobAnalysisRequest request,
        CancellationToken cancellationToken);
}

public sealed record JobAnalyzerCapabilities(
    string Provider,
    bool IsEnabled,
    bool SupportsStructuredOutput,
    int MaximumInputCharacters,
    int MaximumOutputCharacters);

public sealed record JobAnalyzerAvailability(
    bool IsAvailable,
    string StatusCode,
    string? Model);

public sealed record JobAnalysisRequest(
    Guid JobId,
    Guid CandidateProfileSnapshotId,
    int JobRevisionNumber,
    string SchemaVersion,
    string RubricVersion,
    DateTimeOffset DeadlineUtc,
    IReadOnlyList<JobAnalysisCriterionDefinition> Criteria,
    IReadOnlyList<JobAnalysisEvidenceFragment> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record JobAnalysisCriterionDefinition(string Id, int Weight);

public sealed record JobAnalysisEvidenceFragment(
    string Id,
    JobAnalysisEvidenceSource Source,
    string Content);

public enum JobAnalysisEvidenceSource
{
    Profile = 0,
    Job = 1
}

public sealed class JobAnalysisSubmission
{
    [JsonRequired]
    [Description("Overall confidence from 0 through 1.")]
    public double? Confidence { get; init; }

    [JsonRequired]
    [Description("Concise evidence-grounded fit summary.")]
    public string? Summary { get; init; }

    [JsonRequired]
    [Description("One result for every requested criterion.")]
    public List<JobAnalysisCriterionSubmission>? Criteria { get; init; }
}

public sealed class JobAnalysisCriterionSubmission
{
    [JsonRequired]
    [Description("Criterion ID exactly as supplied in the request.")]
    public string? CriterionId { get; init; }

    [JsonRequired]
    [Description("Criterion fit score from 0 through 100.")]
    public int? Score { get; init; }

    [JsonRequired]
    [Description("Criterion confidence from 0 through 1.")]
    public double? Confidence { get; init; }

    [JsonRequired]
    [Description("Only evidence IDs supplied in the request.")]
    public List<string>? EvidenceIds { get; init; }

    [JsonRequired]
    [Description("Concise evidence-grounded mismatch reasons.")]
    public List<string>? MismatchReasons { get; init; }

    [JsonRequired]
    [Description("True when the supplied evidence cannot support this criterion.")]
    public bool? InsufficientEvidence { get; init; }
}

public sealed record JobAnalysisOutput(
    int Score,
    double Confidence,
    string Summary,
    IReadOnlyList<JobAnalysisCriterionResult> Criteria);

public sealed record JobAnalysisCriterionResult(
    string CriterionId,
    int Score,
    double Confidence,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> MismatchReasons,
    bool InsufficientEvidence);

public sealed record JobAnalysisUsage(
    int InputCharacters,
    int OutputCharacters,
    long? InputTokens,
    long? OutputTokens,
    double? AiCredits);

public enum JobAnalysisStatus
{
    Succeeded = 0,
    Disabled = 1,
    InsufficientConfidence = 2,
    InvalidOutput = 3,
    TimedOut = 4,
    Unavailable = 5,
    Refused = 6,
    OverBudget = 7,
    TransientFailure = 8,
    PermanentFailure = 9
}

public sealed record JobAnalysisResult(
    JobAnalysisStatus Status,
    string Provider,
    string? Model,
    string SchemaVersion,
    string RubricVersion,
    JobAnalysisOutput? Output,
    JobAnalysisUsage Usage,
    IReadOnlyList<string> Warnings,
    string? FailureCode)
{
    public bool IsSuccessful => Status == JobAnalysisStatus.Succeeded && Output is not null;

    public JobAnalysisResult MarkInsufficientConfidence() =>
        this with
        {
            Status = JobAnalysisStatus.InsufficientConfidence,
            FailureCode = "BelowMinimumConfidence"
        };

    public static JobAnalysisResult Failure(
        JobAnalysisStatus status,
        string provider,
        string? model,
        string schemaVersion,
        string rubricVersion,
        string failureCode,
        JobAnalysisUsage? usage = null,
        IReadOnlyList<string>? warnings = null) =>
        new(
            status,
            provider,
            model,
            schemaVersion,
            rubricVersion,
            null,
            usage ?? new JobAnalysisUsage(0, 0, null, null, null),
            warnings ?? [],
            failureCode);
}
