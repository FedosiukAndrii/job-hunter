using JobHunter.Application.Profiles;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfCandidateProfileStoreTests
{
    [Fact]
    public async Task ChangedProfileCreatesNewVersionWithoutMutatingHistory()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<ICandidateProfileStore>();
        var profile = new CandidateProfile
        {
            TargetTitles = ["Backend Engineer"],
            Skills = [new CandidateSkill { Name = ".NET", Required = true }]
        };
        var firstDocument = new LoadedCandidateProfile(
            profile,
            "{\"version\":1}",
            "sha256:first",
            "C:\\profiles\\profile.yaml",
            null);
        var secondDocument = firstDocument with
        {
            CanonicalJson = "{\"version\":2}",
            ContentHash = "sha256:second"
        };

        var first = await store.SaveAsync(
            firstDocument,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None);
        var repeated = await store.SaveAsync(
            firstDocument,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CancellationToken.None);
        var second = await store.SaveAsync(
            secondDocument,
            DateTimeOffset.UnixEpoch.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(first, repeated);
        Assert.Equal(1, first.ProfileVersion);
        Assert.Equal(2, second.ProfileVersion);
        Assert.NotEqual(first.Id, second.Id);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        Assert.Equal(2, await context.CandidateProfiles.CountAsync());
    }
}
