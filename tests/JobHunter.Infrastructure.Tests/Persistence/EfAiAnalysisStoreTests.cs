using JobHunter.AI.Abstractions;
using JobHunter.Application.Evaluation;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfAiAnalysisStoreTests
{
    [Fact]
    public async Task SaveIsIdempotentAndRoundTripsValidatedResult()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.Parse(
            "2026-09-16T14:30:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var (jobId, profileId) = await AddReferencesAsync(host, now);
        var store = host.Services.GetRequiredService<IAiAnalysisStore>();
        var result = CreateResult();

        await store.SaveAsync(
            jobId,
            profileId,
            1,
            result,
            now,
            CancellationToken.None);
        await store.SaveAsync(
            jobId,
            profileId,
            1,
            result,
            now.AddMinutes(1),
            CancellationToken.None);
        var loaded = await store.GetAsync(
            jobId,
            profileId,
            1,
            "copilot",
            JobAnalysisSchema.Version,
            "rules-v1",
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(JobAnalysisStatus.Succeeded, loaded.Result.Status);
        Assert.Equal(88, loaded.Result.Output!.Score);
        await using var context = await host.Services
            .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
            .CreateDbContextAsync();
        Assert.Equal(1, await context.AiAnalyses.CountAsync());
    }

    private static JobAnalysisResult CreateResult() =>
        new(
            JobAnalysisStatus.Succeeded,
            "copilot",
            "test-model",
            JobAnalysisSchema.Version,
            "rules-v1",
            new JobAnalysisOutput(
                88,
                0.8,
                "Strong fit.",
                [
                    new JobAnalysisCriterionResult(
                        "coreSkills",
                        88,
                        0.8,
                        ["job:description"],
                        [],
                        false)
                ]),
            new JobAnalysisUsage(500, 200, 100, 40, null),
            [],
            null);

    private static async Task<(Guid JobId, Guid ProfileId)> AddReferencesAsync(
        PersistenceTestHost host,
        DateTimeOffset now)
    {
        await using var context = await host.Services
            .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
            .CreateDbContextAsync();
        var job = JobHunter.Domain.Jobs.Job.Create(
            SourceName.Dou,
            "ai-analysis",
            "https://jobs.dou.ua/vacancies/987/",
            "https://jobs.dou.ua/vacancies/987/",
            null,
            "Senior .NET Engineer",
            "Example",
            string.Empty,
            ".NET role",
            "[]",
            WorkplaceMode.Remote,
            EmploymentType.FullTime,
            "Senior",
            "[\".NET\"]",
            "[\".NET\"]",
            null,
            null,
            null,
            CompensationPeriod.Unknown,
            now,
            PublishedAtPrecision.DateTime,
            "sha256:content",
            "v1:fingerprint",
            1,
            now);
        var profile = CandidateProfileSnapshot.Create(
            1,
            1,
            "sha256:profile",
            "{}",
            null,
            "C:\\profiles\\profile.yaml",
            now);
        context.Jobs.Add(job);
        context.CandidateProfiles.Add(profile);
        await context.SaveChangesAsync();
        return (job.Id, profile.Id);
    }
}
