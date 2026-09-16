using System.Data;
using JobHunter.Application.Profiles;
using JobHunter.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfCandidateProfileStore(
    IDbContextFactory<JobHunterDbContext> contextFactory)
    : ICandidateProfileStore
{
    public async Task<CandidateProfileSnapshotReference> SaveAsync(
        LoadedCandidateProfile profile,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var existing = await context.CandidateProfiles.SingleOrDefaultAsync(
            candidate => candidate.ContentHash == profile.ContentHash,
            cancellationToken);
        if (existing is not null)
        {
            return ToReference(existing);
        }

        var latestVersion = await context.CandidateProfiles
            .Select(candidate => (int?)candidate.ProfileVersion)
            .MaxAsync(cancellationToken)
            ?? 0;
        var snapshot = CandidateProfileSnapshot.Create(
            checked(latestVersion + 1),
            profile.Profile.SchemaVersion,
            profile.ContentHash,
            profile.CanonicalJson,
            profile.SupplementalCvRedacted,
            profile.SourcePath,
            now);
        context.CandidateProfiles.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToReference(snapshot);
    }

    private static CandidateProfileSnapshotReference ToReference(
        CandidateProfileSnapshot snapshot) =>
        new(
            snapshot.Id,
            snapshot.ProfileVersion,
            snapshot.SchemaVersion,
            snapshot.ContentHash);
}
