namespace JobHunter.Domain.AI;

public sealed class AiAnalysis
{
    private AiAnalysis()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Guid CandidateProfileSnapshotId { get; private set; }

    public int JobRevisionNumber { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string? Model { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public string SchemaVersion { get; private set; } = string.Empty;

    public string RubricVersion { get; private set; } = string.Empty;

    public string? ResultJson { get; private set; }

    public string? UsageJson { get; private set; }

    public string? WarningCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AiAnalysis Create(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        string provider,
        string? model,
        string status,
        string schemaVersion,
        string rubricVersion,
        string resultJson,
        string usageJson,
        string? warningCode,
        DateTimeOffset now)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A job ID is required.", nameof(jobId));
        }

        if (candidateProfileSnapshotId == Guid.Empty)
        {
            throw new ArgumentException(
                "A candidate profile snapshot ID is required.",
                nameof(candidateProfileSnapshotId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(jobRevisionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(rubricVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(usageJson);

        return new AiAnalysis
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            CandidateProfileSnapshotId = candidateProfileSnapshotId,
            JobRevisionNumber = jobRevisionNumber,
            Provider = provider,
            Model = model,
            Status = status,
            SchemaVersion = schemaVersion,
            RubricVersion = rubricVersion,
            ResultJson = resultJson,
            UsageJson = usageJson,
            WarningCode = warningCode,
            CreatedAtUtc = now
        };
    }

    public void ReplaceResult(
        string? model,
        string status,
        string resultJson,
        string usageJson,
        string? warningCode,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(usageJson);

        Model = model;
        Status = status;
        ResultJson = resultJson;
        UsageJson = usageJson;
        WarningCode = warningCode;
        CreatedAtUtc = now;
    }
}
