using JobHunter.Application.Profiles;

namespace JobHunter.Application.Tests.Profiles;

public sealed class CandidateProfileValidatorTests
{
    [Fact]
    public void ValidateReportsFieldPathAndRemediation()
    {
        var profile = ValidProfile();
        profile.Scoring.Weights.Compensation = 4;
        profile.Skills.Add(
            new CandidateSkill
            {
                Name = ".net",
                Category = SkillCategory.Core
            });

        var exception = Assert.Throws<CandidateProfileValidationException>(
            () => CandidateProfileValidator.Validate(profile));

        Assert.Contains(exception.Errors, error => error.Path == "$.scoring.weights");
        Assert.Contains(exception.Errors, error => error.Path == "$.skills[2].name");
        Assert.All(exception.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Remediation)));
    }

    internal static CandidateProfile ValidProfile() =>
        new()
        {
            TargetTitles = ["Backend Engineer"],
            Skills =
            [
                new CandidateSkill
                {
                    Name = ".NET",
                    Category = SkillCategory.Core,
                    Required = true,
                    Aliases = ["dotnet"]
                },
                new CandidateSkill
                {
                    Name = "PostgreSQL",
                    Category = SkillCategory.Related
                }
            ],
            RolePreferences = new RolePreferences
            {
                Seniorities = ["Senior"],
                Locations = ["Ukraine"],
                RemotePolicy = RemotePolicy.RemoteOnly,
                EmploymentTypes = [Domain.Jobs.EmploymentType.FullTime]
            },
            Languages =
            [
                new LanguagePreference
                {
                    Name = "English",
                    MinimumLevel = "B2"
                }
            ],
            PreferredDomains = ["FinTech"],
            Salary = new SalaryExpectation
            {
                Minimum = 4000,
                Currency = "USD",
                Period = Domain.Jobs.CompensationPeriod.Month
            },
            Scoring = new ScoringPreferences()
        };
}
