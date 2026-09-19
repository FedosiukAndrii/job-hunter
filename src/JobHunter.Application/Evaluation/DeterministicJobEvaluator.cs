using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Evaluation;

public sealed partial class DeterministicJobEvaluator
{
    public const string RubricVersion = "rules-v3";

    private readonly string _rubricVersion;

    public DeterministicJobEvaluator()
    {
        _rubricVersion = RubricVersion;
    }

    public DeterministicEvaluation Evaluate(
        CandidateProfile profile,
        JobSourceRecord job)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(job);
        CandidateProfileValidator.Validate(profile);

        var searchableText = Normalize(
            string.Join(
                ' ',
                job.Title,
                job.Company,
                job.DescriptionText,
                string.Join(' ', job.Skills),
                string.Join(' ', job.Categories)));
        var hardFilters = EvaluateHardFilters(profile, job, searchableText);
        var missingData = new HashSet<string>(StringComparer.Ordinal);
        var criteria = new List<CriterionEvaluation>
        {
            ScoreSkills(
                "coreSkills",
                profile.Skills.Where(skill => skill.Category == SkillCategory.Core).ToList(),
                profile.Scoring.Weights.CoreSkills,
                searchableText,
                missingData),
            ScoreSeniority(profile, job, missingData),
            ScoreSkills(
                "relatedStack",
                profile.Skills.Where(skill => skill.Category == SkillCategory.Related).ToList(),
                profile.Scoring.Weights.RelatedStack,
                searchableText,
                missingData),
            ScoreRole(profile, job, searchableText, missingData),
            ScoreLocationAndLanguage(profile, job, searchableText, missingData),
            ScoreDomain(profile, searchableText, missingData),
            ScoreCompensation(profile, job, missingData)
        };

        var total = criteria.Sum(criterion => criterion.AwardedPoints);
        var score = new JobScore(Math.Clamp(total, 0, 100));
        var passedHardFilters = hardFilters.All(result => result.Outcome != RuleOutcome.Failed);
        var explanation = BuildExplanation(passedHardFilters, score, hardFilters, criteria, missingData);

        return new DeterministicEvaluation(
            _rubricVersion,
            passedHardFilters,
            score,
            profile.Scoring.RulesOnlyThreshold,
            profile.Scoring.RulesAndAiThreshold,
            hardFilters,
            criteria,
            [.. missingData.Order(StringComparer.Ordinal)],
            explanation);
    }

    private static List<HardFilterResult> EvaluateHardFilters(
        CandidateProfile profile,
        JobSourceRecord job,
        string searchableText)
    {
        var results = new List<HardFilterResult>();

        foreach (var employer in profile.ExcludedEmployers)
        {
            if (ContainsTerm(Normalize(job.Company), employer))
            {
                results.Add(
                    Failed(
                        "excluded-employer",
                        "ExcludedEmployer",
                        "job:company",
                        $"Employer matches excluded value '{employer}'."));
            }
        }

        foreach (var keyword in profile.ExcludedKeywords)
        {
            if (ContainsTerm(searchableText, keyword))
            {
                results.Add(
                    Failed(
                        "excluded-keyword",
                        "ExcludedKeyword",
                        "job:text",
                        $"Vacancy text contains excluded keyword '{keyword}'."));
            }
        }

        foreach (var skill in profile.Skills.Where(skill => skill.Required))
        {
            if (!SkillMatches(searchableText, skill))
            {
                results.Add(
                    Failed(
                        "mandatory-skill",
                        "MissingMandatorySkill",
                        $"profile:skill:{Slug(skill.Name)}",
                        $"Required skill '{skill.Name}' is not stated in the vacancy."));
            }
        }

        var senioritySelection = EvaluateSenioritySelectionRules(
            profile.RolePreferences.SenioritySelectionRules,
            job,
            searchableText);
        if (senioritySelection is not null)
        {
            results.Add(senioritySelection);
        }

        var remoteResult = EvaluateRemotePolicy(profile.RolePreferences.RemotePolicy, job.WorkplaceMode);
        if (remoteResult is not null)
        {
            results.Add(remoteResult);
        }

        if (profile.RolePreferences.Locations.Count > 0
            && job.WorkplaceMode != WorkplaceMode.Remote
            && job.Locations.Count > 0
            && !profile.RolePreferences.Locations.Any(
                preferred => job.Locations.Any(location => ContainsTerm(Normalize(location), preferred))))
        {
            results.Add(
                Failed(
                    "location",
                    "LocationMismatch",
                    "job:locations",
                    "The explicitly stated job location is outside configured locations."));
        }

        if (profile.RolePreferences.EmploymentTypes.Count > 0
            && job.EmploymentType != EmploymentType.Unknown
            && !profile.RolePreferences.EmploymentTypes.Contains(job.EmploymentType))
        {
            results.Add(
                Failed(
                    "employment-type",
                    "EmploymentTypeMismatch",
                    "job:employment-type",
                    "The explicitly stated employment type is not accepted."));
        }

        var salary = profile.Salary;
        if (salary is not null
            && job.CompensationMaximum is not null
            && string.Equals(
                salary.Currency,
                job.CompensationCurrency,
                StringComparison.OrdinalIgnoreCase)
            && salary.Period == job.CompensationPeriod
            && job.CompensationMaximum < salary.Minimum)
        {
            results.Add(
                Failed(
                    "salary-floor",
                    "BelowSalaryFloor",
                    "job:compensation",
                    "The explicitly stated maximum compensation is below the configured floor."));
        }

        if (results.Count == 0)
        {
            results.Add(
                new HardFilterResult(
                    "hard-filters",
                    RuleOutcome.Passed,
                    null,
                    [],
                    "No configured hard filter rejected the vacancy."));
        }

        return results;
    }

    private static HardFilterResult? EvaluateSenioritySelectionRules(
        List<SenioritySelectionRule> rules,
        JobSourceRecord job,
        string searchableText)
    {
        if (rules.Count == 0)
        {
            return null;
        }

        // DOU does not always expose a separate seniority field. A title that
        // explicitly names a level is still evidence; this does not infer one.
        var seniorityEvidence = string.IsNullOrWhiteSpace(job.Seniority)
            ? Normalize(job.Title)
            : Normalize(job.Seniority);
        var seniorityEvidenceId = string.IsNullOrWhiteSpace(job.Seniority)
            ? "job:title"
            : "job:seniority";
        foreach (var rule in rules)
        {
            if (rule.MinimumRequiredExperienceYears is int minimumExperience
                && HasExplicitMinimumExperience(
                    job,
                    minimumExperience))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(rule.Seniority)
                && ContainsTerm(seniorityEvidence, rule.Seniority)
                && (rule.RequiredAnyKeywords.Count == 0
                    || rule.RequiredAnyKeywords.Any(
                        keyword => ContainsTerm(searchableText, keyword))))
            {
                return null;
            }
        }

        return Failed(
            "seniority-selection",
            "SenioritySelectionMismatch",
            seniorityEvidenceId,
            "The vacancy does not match a configured seniority selection rule.");
    }

    private static bool HasExplicitMinimumExperience(
        JobSourceRecord job,
        int minimumRequiredExperienceYears)
    {
        // A bare "4 years" is deliberately not accepted: it could describe a
        // product, company, or candidate rather than an eligibility requirement.
        // Only unambiguous threshold formulations are recognized.
        var experienceText = $"{job.Title}\n{job.DescriptionText}";
        foreach (Match match in ExplicitExperienceRequirementPattern().Matches(experienceText))
        {
            if (int.TryParse(
                    match.Groups["years"].Value,
                    CultureInfo.InvariantCulture,
                    out var years)
                && years >= minimumRequiredExperienceYears)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(
        "(?<!\\d)(?<years>\\d{1,2})\\s*\\+\\s*(?:years?|yrs?)(?:\\s+(?:of\\s+)?experience)?\\b|\\bat\\s+least\\s+(?<years>\\d{1,2})\\s*(?:years?|yrs?)(?:\\s+(?:of\\s+)?experience)?\\b|\\b(?:minimum\\s+of|minimum)\\s+(?<years>\\d{1,2})\\s*(?:years?|yrs?)(?:\\s+(?:of\\s+)?experience)?\\b|(?:від|не\\s+менше\\s+ніж)\\s+(?<years>\\d{1,2})\\s+(?:роки|років)\\s+досвіду",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitExperienceRequirementPattern();

    private static HardFilterResult? EvaluateRemotePolicy(
        RemotePolicy policy,
        WorkplaceMode workplaceMode) =>
        (policy, workplaceMode) switch
        {
            (RemotePolicy.RemoteOnly, WorkplaceMode.OnSite or WorkplaceMode.Hybrid) =>
                Failed(
                    "remote-policy",
                    "RemotePolicyMismatch",
                    "job:workplace-mode",
                    "The vacancy explicitly requires non-remote attendance."),
            (RemotePolicy.RemoteOrHybrid, WorkplaceMode.OnSite) =>
                Failed(
                    "remote-policy",
                    "RemotePolicyMismatch",
                    "job:workplace-mode",
                    "The vacancy explicitly requires on-site work."),
            (RemotePolicy.OnSiteOnly, WorkplaceMode.Remote) =>
                Failed(
                    "remote-policy",
                    "RemotePolicyMismatch",
                    "job:workplace-mode",
                    "The vacancy is explicitly remote-only."),
            _ => null
        };

    private static CriterionEvaluation ScoreSkills(
        string criterionId,
        List<CandidateSkill> skills,
        int maximumPoints,
        string searchableText,
        HashSet<string> missingData)
    {
        if (maximumPoints == 0)
        {
            return new CriterionEvaluation(criterionId, 0, 0, [], false);
        }

        if (skills.Count == 0)
        {
            return new CriterionEvaluation(criterionId, maximumPoints, maximumPoints, [], false);
        }

        var matched = skills
            .Where(skill => SkillMatches(searchableText, skill))
            .ToList();
        if (matched.Count == 0)
        {
            missingData.Add($"job:{criterionId}");
        }

        var awarded = WeightedPoints(maximumPoints, matched.Count, skills.Count);
        return new CriterionEvaluation(
            criterionId,
            awarded,
            maximumPoints,
            matched.Select(skill => $"profile:skill:{Slug(skill.Name)}").ToArray(),
            matched.Count == 0);
    }

    private static CriterionEvaluation ScoreSeniority(
        CandidateProfile profile,
        JobSourceRecord job,
        HashSet<string> missingData)
    {
        var maximum = profile.Scoring.Weights.Seniority;
        if (profile.RolePreferences.Seniorities.Count == 0)
        {
            return new CriterionEvaluation("seniority", maximum, maximum, [], false);
        }

        if (string.IsNullOrWhiteSpace(job.Seniority))
        {
            missingData.Add("job:seniority");
            return new CriterionEvaluation("seniority", 0, maximum, [], true);
        }

        var matches = profile.RolePreferences.Seniorities.Any(
            preferred => ContainsTerm(Normalize(job.Seniority), preferred));
        return new CriterionEvaluation(
            "seniority",
            matches ? maximum : 0,
            maximum,
            matches ? ["job:seniority"] : [],
            false);
    }

    private static CriterionEvaluation ScoreRole(
        CandidateProfile profile,
        JobSourceRecord job,
        string searchableText,
        HashSet<string> missingData)
    {
        var maximum = profile.Scoring.Weights.RoleResponsibilities;
        var titleText = Normalize(job.Title);
        var titleMatch = profile.TargetTitles.Any(title => ContainsTerm(titleText, title));
        var bodyMatch = profile.TargetTitles.Any(title => ContainsTerm(searchableText, title));

        if (!titleMatch && !bodyMatch)
        {
            missingData.Add("job:target-role");
        }

        return new CriterionEvaluation(
            "roleResponsibilities",
            titleMatch ? maximum : bodyMatch ? maximum / 2 : 0,
            maximum,
            titleMatch ? ["job:title"] : bodyMatch ? ["job:text"] : [],
            !titleMatch && !bodyMatch);
    }

    private static CriterionEvaluation ScoreLocationAndLanguage(
        CandidateProfile profile,
        JobSourceRecord job,
        string searchableText,
        HashSet<string> missingData)
    {
        var maximum = profile.Scoring.Weights.LocationLanguage;
        var hasLocationRequirement = profile.RolePreferences.Locations.Count > 0
            || profile.RolePreferences.RemotePolicy != RemotePolicy.Any;
        var hasLanguageRequirement = profile.Languages.Count > 0;
        if (!hasLocationRequirement && !hasLanguageRequirement)
        {
            return new CriterionEvaluation("locationLanguage", maximum, maximum, [], false);
        }

        var dimensions = 0;
        var matches = 0;
        var evidence = new List<string>();

        if (hasLocationRequirement)
        {
            dimensions++;
            var locationMatches = job.WorkplaceMode == WorkplaceMode.Remote
                && profile.RolePreferences.RemotePolicy != RemotePolicy.OnSiteOnly
                || profile.RolePreferences.Locations.Any(
                    preferred => job.Locations.Any(
                        location => ContainsTerm(Normalize(location), preferred)));
            if (locationMatches)
            {
                matches++;
                evidence.Add(
                    job.WorkplaceMode == WorkplaceMode.Remote
                        ? "job:workplace-mode"
                        : "job:locations");
            }
            else if (job.WorkplaceMode == WorkplaceMode.Unknown && job.Locations.Count == 0)
            {
                missingData.Add("job:location-workplace");
            }
        }

        if (hasLanguageRequirement)
        {
            dimensions++;
            var languageMatches = profile.Languages.Count(
                language => ContainsTerm(searchableText, language.Name));
            if (languageMatches == profile.Languages.Count)
            {
                matches++;
                evidence.Add("job:text");
            }
            else
            {
                missingData.Add("job:languages");
            }
        }

        return new CriterionEvaluation(
            "locationLanguage",
            WeightedPoints(maximum, matches, dimensions),
            maximum,
            evidence,
            evidence.Count == 0);
    }

    private static CriterionEvaluation ScoreDomain(
        CandidateProfile profile,
        string searchableText,
        HashSet<string> missingData)
    {
        var maximum = profile.Scoring.Weights.Domain;
        if (profile.PreferredDomains.Count == 0)
        {
            return new CriterionEvaluation("domain", maximum, maximum, [], false);
        }

        var matched = profile.PreferredDomains
            .Where(domain => ContainsTerm(searchableText, domain))
            .ToArray();
        if (matched.Length == 0)
        {
            missingData.Add("job:domain");
        }

        return new CriterionEvaluation(
            "domain",
            WeightedPoints(maximum, matched.Length, profile.PreferredDomains.Count),
            maximum,
            matched.Select(domain => $"profile:domain:{Slug(domain)}").ToArray(),
            matched.Length == 0);
    }

    private static CriterionEvaluation ScoreCompensation(
        CandidateProfile profile,
        JobSourceRecord job,
        HashSet<string> missingData)
    {
        var maximum = profile.Scoring.Weights.Compensation;
        if (profile.Salary is null)
        {
            return new CriterionEvaluation("compensation", maximum, maximum, [], false);
        }

        if (job.CompensationMaximum is null
            || job.CompensationCurrency is null
            || job.CompensationPeriod == CompensationPeriod.Unknown)
        {
            missingData.Add("job:compensation");
            return new CriterionEvaluation("compensation", 0, maximum, [], true);
        }

        var comparable = string.Equals(
                profile.Salary.Currency,
                job.CompensationCurrency,
                StringComparison.OrdinalIgnoreCase)
            && profile.Salary.Period == job.CompensationPeriod;
        if (!comparable)
        {
            missingData.Add("job:compensation-comparability");
            return new CriterionEvaluation("compensation", 0, maximum, [], true);
        }

        var meetsFloor = job.CompensationMaximum >= profile.Salary.Minimum;
        return new CriterionEvaluation(
            "compensation",
            meetsFloor ? maximum : 0,
            maximum,
            ["job:compensation"],
            false);
    }

    private static string BuildExplanation(
        bool passedHardFilters,
        JobScore score,
        IReadOnlyCollection<HardFilterResult> hardFilters,
        IReadOnlyCollection<CriterionEvaluation> criteria,
        HashSet<string> missingData)
    {
        var builder = new StringBuilder();
        builder.Append(passedHardFilters ? "Hard filters passed." : "Hard filters rejected the vacancy.");
        builder.Append(
            CultureInfo.InvariantCulture,
            $" Deterministic score: {score.Value}/100.");

        var failedReasonCodes = hardFilters
            .Where(result => result.Outcome == RuleOutcome.Failed)
            .Select(result => result.ReasonCode)
            .Where(code => code is not null)
            .ToArray();
        if (failedReasonCodes.Length > 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" Reasons: {string.Join(", ", failedReasonCodes)}.");
        }

        var strongest = criteria
            .Where(criterion => criterion.AwardedPoints > 0)
            .OrderByDescending(criterion => criterion.AwardedPoints)
            .ThenBy(criterion => criterion.CriterionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (strongest is not null)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" Strongest criterion: {strongest.CriterionId} ({strongest.AwardedPoints}/{strongest.MaximumPoints}).");
        }

        if (missingData.Count > 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" Missing explicit data: {string.Join(", ", missingData.Order(StringComparer.Ordinal))}.");
        }

        return builder.ToString();
    }

    private static HardFilterResult Failed(
        string ruleId,
        string reasonCode,
        string evidenceId,
        string explanation) =>
        new(ruleId, RuleOutcome.Failed, reasonCode, [evidenceId], explanation);

    private static bool SkillMatches(string normalizedText, CandidateSkill skill) =>
        ContainsTerm(normalizedText, skill.Name)
        || skill.Aliases.Any(alias => ContainsTerm(normalizedText, alias));

    private static bool ContainsTerm(string normalizedText, string term)
    {
        var normalizedTerm = Normalize(term);
        return normalizedTerm.Length > 0
            && $" {normalizedText} ".Contains(
                $" {normalizedTerm} ",
                StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character) || character is '+' or '#' or '.')
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string Slug(string value) =>
        Normalize(value).Replace(' ', '-');

    private static int WeightedPoints(int maximum, int matched, int total) =>
        total == 0
            ? maximum
            : (int)Math.Round(
                maximum * (decimal)matched / total,
                MidpointRounding.AwayFromZero);
}

public enum RuleOutcome
{
    Passed = 0,
    Failed = 1,
    NotEvaluated = 2
}

public sealed record HardFilterResult(
    string RuleId,
    RuleOutcome Outcome,
    string? ReasonCode,
    IReadOnlyList<string> EvidenceIds,
    string Explanation);

public sealed record CriterionEvaluation(
    string CriterionId,
    int AwardedPoints,
    int MaximumPoints,
    IReadOnlyList<string> EvidenceIds,
    bool HasMissingData);

public sealed record DeterministicEvaluation(
    string RubricVersion,
    bool PassedHardFilters,
    JobScore Score,
    int RulesOnlyThreshold,
    int RulesAndAiThreshold,
    IReadOnlyList<HardFilterResult> HardFilters,
    IReadOnlyList<CriterionEvaluation> Criteria,
    IReadOnlyList<string> MissingData,
    string Explanation);
