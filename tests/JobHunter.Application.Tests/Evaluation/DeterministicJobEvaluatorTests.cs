using JobHunter.Application.Evaluation;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Application.Tests.Profiles;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;

namespace JobHunter.Application.Tests.Evaluation;

public sealed class DeterministicJobEvaluatorTests
{
    [Fact]
    public void EvaluatePassesWhenExplicitHardFiltersMatch()
    {
        var first = DeterministicJobEvaluator.Evaluate(
            CandidateProfileValidatorTests.ValidProfile(),
            MatchingJob());
        var second = DeterministicJobEvaluator.Evaluate(
            CandidateProfileValidatorTests.ValidProfile(),
            MatchingJob());

        Assert.True(first.PassedHardFilters);
        Assert.Equal(first.HardFilters, second.HardFilters);
        Assert.Equal(
            "Hard filters passed. AI evaluation is required for qualification.",
            first.Explanation);
    }

    [Fact]
    public void EvaluateReturnsExplicitHardFilterReasons()
    {
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.HardFilters.ExcludedEmployers.Add("Blocked Corp");
        var job = MatchingJob() with
        {
            Company = "Blocked Corp",
            DescriptionText = "Java role in an office.",
            DescriptionHtml = "<p>Java role in an office.</p>",
            Skills = ["Java"],
            Categories = ["Java"],
            Locations = ["Warsaw"],
            WorkplaceMode = WorkplaceMode.OnSite
        };

        var result = DeterministicJobEvaluator.Evaluate(profile, job);
        var reasonCodes = result.HardFilters
            .Select(filter => filter.ReasonCode)
            .Where(code => code is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.False(result.PassedHardFilters);
        Assert.Contains("ExcludedEmployer", reasonCodes);
        Assert.Contains("MissingRequiredSkill", reasonCodes);
        Assert.Contains("RemotePolicyMismatch", reasonCodes);
        Assert.Contains("LocationMismatch", reasonCodes);
    }

    [Fact]
    public void EvaluateDoesNotInferAbsentLocation()
    {
        var job = MatchingJob() with
        {
            Locations = [],
            WorkplaceMode = WorkplaceMode.Unknown
        };

        var result = DeterministicJobEvaluator.Evaluate(
            CandidateProfileValidatorTests.ValidProfile(),
            job);

        Assert.True(result.PassedHardFilters);
        Assert.DoesNotContain(
            result.HardFilters,
            filter => filter.ReasonCode == "LocationMismatch");
    }

    private static JobSourceRecord MatchingJob() =>
        new()
        {
            Source = SourceName.Dou,
            SourceJobId = NativeSourceId.Create("123"),
            SourceUrl = new Uri("https://jobs.dou.ua/vacancies/123/"),
            CanonicalUrl = CanonicalJobUrl.Create("https://jobs.dou.ua/vacancies/123/"),
            Title = "Senior Backend Engineer",
            Company = "Example",
            DescriptionHtml = "<p>Build .NET and C# services.</p>",
            DescriptionText = "Build .NET and C# services.",
            Locations = ["Ukraine"],
            WorkplaceMode = WorkplaceMode.Remote,
            EmploymentType = EmploymentType.FullTime,
            Seniority = "Senior",
            Skills = [".NET", "C#"],
            Categories = [".NET"],
            CompensationPeriod = CompensationPeriod.Unknown,
            PublishedAtPrecision = PublishedAtPrecision.DateTime,
            ParserVersion = "test",
            ContentHash = "content",
            RawPayloadHash = "payload",
            RetrievedAtUtc = DateTimeOffset.UnixEpoch
        };
}
