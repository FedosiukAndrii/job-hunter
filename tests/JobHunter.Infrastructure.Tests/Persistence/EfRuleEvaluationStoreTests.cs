using System.Text.Json;
using JobHunter.Application.Evaluation;
using JobHunter.Application.Profiles;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfRuleEvaluationStoreTests
{
    [Fact]
    public async Task ProfileChangeCreatesNewEvaluationAndKeepsHistoricalResult()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var profileStore = host.Services.GetRequiredService<ICandidateProfileStore>();
        var evaluationStore = host.Services.GetRequiredService<IRuleEvaluationStore>();
        var now = DateTimeOffset.UnixEpoch;
        var firstProfile = await profileStore.SaveAsync(
            ProfileDocument("sha256:profile-1"),
            now,
            CancellationToken.None);
        var secondProfile = await profileStore.SaveAsync(
            ProfileDocument("sha256:profile-2"),
            now.AddMinutes(1),
            CancellationToken.None);
        var jobId = await AddJobAsync(host, now);
        var evaluation = new DeterministicEvaluation(
            DeterministicJobEvaluator.RubricVersion,
            true,
            [
                new HardFilterResult(
                    "required-skill",
                    RuleOutcome.Passed,
                    null,
                    ["profile:required-skills"],
                    "Required skill was found.")
            ],
            "Hard filters passed. AI evaluation is required for qualification.");

        var firstId = await evaluationStore.SaveAsync(
            jobId,
            firstProfile.Id,
            0,
            evaluation,
            now,
            CancellationToken.None);
        var repeatedId = await evaluationStore.SaveAsync(
            jobId,
            firstProfile.Id,
            0,
            evaluation,
            now.AddSeconds(1),
            CancellationToken.None);
        var changedProfileId = await evaluationStore.SaveAsync(
            jobId,
            secondProfile.Id,
            0,
            evaluation,
            now.AddSeconds(2),
            CancellationToken.None);

        Assert.Equal(firstId, repeatedId);
        Assert.NotEqual(firstId, changedProfileId);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        Assert.Equal(2, await context.RuleEvaluations.CountAsync());

        var persisted = await context.RuleEvaluations.SingleAsync(
            candidate => candidate.Id == firstId);
        using var ruleResults = JsonDocument.Parse(persisted.RuleResultsJson);
        var hardFilter = Assert.Single(ruleResults.RootElement.EnumerateArray());
        Assert.Equal("required-skill", hardFilter.GetProperty("ruleId").GetString());
        Assert.Equal(
            "Hard filters passed. AI evaluation is required for qualification.",
            persisted.Explanation);
    }

    private static LoadedCandidateProfile ProfileDocument(string hash) =>
        new(
            new CandidateProfile
            {
                TargetTitles = ["Backend Engineer"],
                RequiredSkills = [".NET"]
            },
            "{}",
            hash,
            "C:\\profiles\\profile.yaml",
            null);

    private static async Task<Guid> AddJobAsync(
        PersistenceTestHost host,
        DateTimeOffset now)
    {
        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var job = JobHunter.Domain.Jobs.Job.Create(
            SourceName.Dou,
            "123",
            "https://jobs.dou.ua/vacancies/123/",
            "https://jobs.dou.ua/vacancies/123/",
            null,
            "Backend Engineer",
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
            null,
            PublishedAtPrecision.Unknown,
            "sha256:content",
            "v1:fingerprint",
            1,
            now);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        return job.Id;
    }
}
