namespace JobHunter.Domain.AI;

public sealed class AiAnalysis
{
    private AiAnalysis()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Guid CandidateProfileSnapshotId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string? Model { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public string SchemaVersion { get; private set; } = string.Empty;

    public string RubricVersion { get; private set; } = string.Empty;

    public string? ResultJson { get; private set; }

    public string? UsageJson { get; private set; }

    public string? WarningCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
