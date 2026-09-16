namespace JobHunter.Application.Profiles;

public interface ICandidateProfileLoader
{
    Task<LoadedCandidateProfile> LoadAsync(CancellationToken cancellationToken);
}

public sealed record LoadedCandidateProfile(
    CandidateProfile Profile,
    string CanonicalJson,
    string ContentHash,
    string SourcePath,
    string? SupplementalCvRedacted);

public interface ICandidateProfileStore
{
    Task<CandidateProfileSnapshotReference> SaveAsync(
        LoadedCandidateProfile profile,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record CandidateProfileSnapshotReference(
    Guid Id,
    int ProfileVersion,
    int SchemaVersion,
    string ContentHash);
