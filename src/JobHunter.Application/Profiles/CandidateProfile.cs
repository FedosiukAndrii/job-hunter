using JobHunter.Domain.Jobs;
using System.Text.Json.Serialization;

namespace JobHunter.Application.Profiles;

public sealed class CandidateProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<string> TargetTitles { get; set; } = [];

    public List<CandidateSkill> Skills { get; set; } = [];

    public RolePreferences RolePreferences { get; set; } = new();

    public List<LanguagePreference> Languages { get; set; } = [];

    public List<string> PreferredDomains { get; set; } = [];

    public SalaryExpectation? Salary { get; set; }

    public List<string> ExcludedEmployers { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public ScoringPreferences Scoring { get; set; } = new();

    public string? SupplementalCvPath { get; set; }
}

public sealed class CandidateSkill
{
    public string Name { get; set; } = string.Empty;

    public SkillCategory Category { get; set; } = SkillCategory.Related;

    public bool Required { get; set; }

    public decimal? YearsExperience { get; set; }

    public List<string> Aliases { get; set; } = [];

    public List<string> Evidence { get; set; } = [];
}

public enum SkillCategory
{
    Core = 0,
    Related = 1
}

public sealed class RolePreferences
{
    public List<string> Seniorities { get; set; } = [];

    // Unlike Seniorities, which contributes to the seniority score, these
    // rules are an optional eligibility gate. A vacancy must match one rule
    // before it can qualify when the collection is non-empty.
    public List<SenioritySelectionRule> SenioritySelectionRules { get; set; } = [];

    public List<string> Locations { get; set; } = [];

    public RemotePolicy RemotePolicy { get; set; } = RemotePolicy.Any;

    public List<EmploymentType> EmploymentTypes { get; set; } = [];
}

public sealed class SenioritySelectionRule
{
    public string? Seniority { get; set; }

    // This is an alternative to Seniority: the vacancy must explicitly state
    // a recognized minimum experience requirement at or above this value.
    public int? MinimumRequiredExperienceYears { get; set; }

    // A matching seniority passes without an extra condition when this is
    // empty. Otherwise, at least one term must be explicitly present in the
    // vacancy title, description, skills, or categories.
    public List<string> RequiredAnyKeywords { get; set; } = [];
}

public enum RemotePolicy
{
    Any = 0,
    RemoteOnly = 1,
    RemoteOrHybrid = 2,
    PreferRemote = 3,
    OnSiteOnly = 4
}

public sealed class LanguagePreference
{
    public string Name { get; set; } = string.Empty;

    public string? MinimumLevel { get; set; }

    public bool Required { get; set; }
}

public sealed class SalaryExpectation
{
    public decimal Minimum { get; set; }

    public string Currency { get; set; } = "USD";

    public CompensationPeriod Period { get; set; } = CompensationPeriod.Month;
}

public sealed class ScoringPreferences
{
    public int RulesOnlyThreshold { get; set; } = 72;

    public int RulesAndAiThreshold { get; set; } = 75;

    public ScoringWeights Weights { get; set; } = new();
}

public sealed class ScoringWeights
{
    public int CoreSkills { get; set; } = 30;

    public int Seniority { get; set; } = 15;

    public int RelatedStack { get; set; } = 15;

    public int RoleResponsibilities { get; set; } = 15;

    public int LocationLanguage { get; set; } = 10;

    public int Domain { get; set; } = 10;

    public int Compensation { get; set; } = 5;

    [JsonIgnore]
    public int Total =>
        CoreSkills
        + Seniority
        + RelatedStack
        + RoleResponsibilities
        + LocationLanguage
        + Domain
        + Compensation;
}
