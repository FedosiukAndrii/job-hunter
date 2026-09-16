namespace JobHunter.Domain.Profiles;

public sealed class CandidateProfileSnapshot
{
    private CandidateProfileSnapshot()
    {
    }

    public Guid Id { get; private set; }

    public int ProfileVersion { get; private set; }

    public int SchemaVersion { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string CanonicalJson { get; private set; } = string.Empty;

    public string? SupplementalCvRedacted { get; private set; }

    public string SourcePath { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static CandidateProfileSnapshot Create(
        int profileVersion,
        int schemaVersion,
        string contentHash,
        string canonicalJson,
        string? supplementalCvRedacted,
        string sourcePath,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(profileVersion, 1);

        return new CandidateProfileSnapshot
        {
            Id = Guid.NewGuid(),
            ProfileVersion = profileVersion,
            SchemaVersion = schemaVersion,
            ContentHash = contentHash,
            CanonicalJson = canonicalJson,
            SupplementalCvRedacted = supplementalCvRedacted,
            SourcePath = sourcePath,
            CreatedAtUtc = now
        };
    }
}
