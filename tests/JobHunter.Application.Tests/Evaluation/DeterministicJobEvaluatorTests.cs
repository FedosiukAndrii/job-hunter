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
    public void EvaluateReturnsFullDeterministicScoreForExplicitMatch()
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        var job = MatchingJob();

        var first = evaluator.Evaluate(profile, job);
        var second = evaluator.Evaluate(profile, job);

        Assert.True(first.PassedHardFilters);
        Assert.Equal(100, first.Score.Value);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(
            first.HardFilters.Select(result => result.ToString()),
            second.HardFilters.Select(result => result.ToString()));
        Assert.Equal(
            first.Criteria.Select(result => result.ToString()),
            second.Criteria.Select(result => result.ToString()));
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Empty(first.MissingData);
    }

    [Fact]
    public void EvaluateReturnsExplicitHardFilterReasons()
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.ExcludedEmployers.Add("Blocked Corp");
        var job = MatchingJob() with
        {
            Company = "Blocked Corp",
            DescriptionText = "Java role in an office.",
            DescriptionHtml = "<p>Java role in an office.</p>",
            Skills = ["Java"],
            Categories = ["Java"],
            Locations = ["Warsaw"],
            WorkplaceMode = WorkplaceMode.OnSite,
            CompensationMaximum = 3000
        };

        var result = evaluator.Evaluate(profile, job);
        var reasonCodes = result.HardFilters
            .Select(filter => filter.ReasonCode)
            .Where(code => code is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.False(result.PassedHardFilters);
        Assert.Contains("ExcludedEmployer", reasonCodes);
        Assert.Contains("MissingMandatorySkill", reasonCodes);
        Assert.Contains("RemotePolicyMismatch", reasonCodes);
        Assert.Contains("LocationMismatch", reasonCodes);
        Assert.Contains("BelowSalaryFloor", reasonCodes);
    }

    [Fact]
    public void EvaluateDoesNotInferAbsentSalaryOrLocation()
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        var job = MatchingJob() with
        {
            Locations = [],
            WorkplaceMode = WorkplaceMode.Unknown,
            CompensationMinimum = null,
            CompensationMaximum = null,
            CompensationCurrency = null,
            CompensationPeriod = CompensationPeriod.Unknown
        };

        var result = evaluator.Evaluate(profile, job);

        Assert.True(result.PassedHardFilters);
        Assert.Contains("job:location-workplace", result.MissingData);
        Assert.Contains("job:compensation", result.MissingData);
        Assert.DoesNotContain(
            result.HardFilters,
            filter => filter.ReasonCode is "LocationMismatch" or "BelowSalaryFloor");
    }

    [Fact]
    public void EvaluateUsesSenioritySelectionRuleAsAnEligibilityGate()
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.RolePreferences.SenioritySelectionRules =
        [
            new SenioritySelectionRule { Seniority = "Senior" },
            new SenioritySelectionRule
            {
                Seniority = "Middle",
                RequiredAnyKeywords = ["бронювання", "reservation"]
            }
        ];

        var middleWithReservation = MatchingJob() with
        {
            Seniority = "Middle",
            Title = "Middle Backend Engineer",
            DescriptionText = ".NET C# PostgreSQL FinTech English B2. Є бронювання працівників.",
            DescriptionHtml = "<p>Є бронювання працівників.</p>"
        };
        var middleWithoutReservation = middleWithReservation with
        {
            DescriptionText = ".NET C# PostgreSQL FinTech English B2.",
            DescriptionHtml = "<p>.NET C# PostgreSQL FinTech English B2.</p>"
        };

        Assert.True(evaluator.Evaluate(profile, middleWithReservation).PassedHardFilters);

        var rejected = evaluator.Evaluate(profile, middleWithoutReservation);
        Assert.False(rejected.PassedHardFilters);
        Assert.Contains(
            rejected.HardFilters,
            filter => filter.ReasonCode == "SenioritySelectionMismatch");
    }

    [Fact]
    public void EvaluateUsesExplicitTitleWhenSourceHasNoSeniorityField()
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.RolePreferences.SenioritySelectionRules =
        [new SenioritySelectionRule { Seniority = "Senior" }];

        var result = evaluator.Evaluate(profile, MatchingJob() with { Seniority = null });

        Assert.True(result.PassedHardFilters);
    }

    [Theory]
    [InlineData("Requires 4+ years of experience with .NET.")]
    [InlineData("At least 4 years of .NET experience are required.")]
    [InlineData("Потрібно від 4 років досвіду роботи з .NET.")]
    public void EvaluateAcceptsOnlyExplicitMinimumExperienceFormulations(
        string experienceRequirement)
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.RolePreferences.SenioritySelectionRules =
        [new SenioritySelectionRule { MinimumRequiredExperienceYears = 4 }];
        var job = MatchingJob() with
        {
            Title = "Backend Engineer",
            Seniority = null,
            DescriptionText = experienceRequirement,
            DescriptionHtml = $"<p>{experienceRequirement}</p>"
        };

        Assert.True(evaluator.Evaluate(profile, job).PassedHardFilters);
    }

    [Theory]
    [InlineData("We have built this product for 5 years.")]
    [InlineData("Компанія працює від 5 років.")]
    [InlineData("3+ years of .NET experience are required.")]
    public void EvaluateDoesNotInferMinimumExperience(string description)
    {
        var evaluator = new DeterministicJobEvaluator();
        var profile = CandidateProfileValidatorTests.ValidProfile();
        profile.RolePreferences.SenioritySelectionRules =
        [new SenioritySelectionRule { MinimumRequiredExperienceYears = 4 }];
        var job = MatchingJob() with
        {
            Title = "Backend Engineer",
            Seniority = null,
            DescriptionText = description,
            DescriptionHtml = $"<p>{description}</p>"
        };

        var evaluation = evaluator.Evaluate(profile, job);

        Assert.False(evaluation.PassedHardFilters);
        Assert.Contains(
            evaluation.HardFilters,
            filter => filter.ReasonCode == "SenioritySelectionMismatch");
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
            DescriptionHtml =
                "<p>Build .NET and C# services with PostgreSQL for a FinTech product. English B2.</p>",
            DescriptionText =
                "Build .NET and C# services with PostgreSQL for a FinTech product. English B2.",
            Locations = ["Ukraine"],
            WorkplaceMode = WorkplaceMode.Remote,
            EmploymentType = EmploymentType.FullTime,
            Seniority = "Senior",
            Skills = [".NET", "C#", "PostgreSQL"],
            Categories = [".NET"],
            CompensationMinimum = 4500,
            CompensationMaximum = 5500,
            CompensationCurrency = "USD",
            CompensationPeriod = CompensationPeriod.Month,
            PublishedAtPrecision = PublishedAtPrecision.DateTime,
            ParserVersion = "test",
            ContentHash = "content",
            RawPayloadHash = "payload",
            RetrievedAtUtc = DateTimeOffset.UnixEpoch
        };
}
