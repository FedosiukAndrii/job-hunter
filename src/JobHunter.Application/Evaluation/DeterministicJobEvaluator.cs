using System.Text;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Evaluation;

public static class DeterministicJobEvaluator
{
    public const string RubricVersion = "hard-filters-v1";

    public static DeterministicEvaluation Evaluate(
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
        var filters = EvaluateHardFilters(profile, job, searchableText);
        var passed = filters.All(filter => filter.Outcome != RuleOutcome.Failed);

        return new DeterministicEvaluation(
            RubricVersion,
            passed,
            filters,
            BuildExplanation(passed, filters));
    }

    private static List<HardFilterResult> EvaluateHardFilters(
        CandidateProfile profile,
        JobSourceRecord job,
        string searchableText)
    {
        var filters = profile.HardFilters;
        var results = new List<HardFilterResult>();

        foreach (var employer in filters.ExcludedEmployers)
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

        foreach (var keyword in filters.ExcludedKeywords)
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

        foreach (var skill in profile.RequiredSkills)
        {
            if (!ContainsTerm(searchableText, skill))
            {
                results.Add(
                    Failed(
                        "required-skill",
                        "MissingRequiredSkill",
                        "profile:required-skills",
                        $"Required skill '{skill}' is not stated in the vacancy."));
            }
        }

        var remoteResult = EvaluateRemotePolicy(filters.RemotePolicy, job.WorkplaceMode);
        if (remoteResult is not null)
        {
            results.Add(remoteResult);
        }

        if (filters.Locations.Count > 0
            && job.WorkplaceMode != WorkplaceMode.Remote
            && job.Locations.Count > 0
            && !filters.Locations.Any(
                preferred => job.Locations.Any(
                    location => ContainsTerm(Normalize(location), preferred))))
        {
            results.Add(
                Failed(
                    "location",
                    "LocationMismatch",
                    "job:locations",
                    "The explicitly stated job location is outside configured locations."));
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

    private static string BuildExplanation(
        bool passed,
        IReadOnlyCollection<HardFilterResult> filters)
    {
        var failedReasonCodes = filters
            .Where(filter => filter.Outcome == RuleOutcome.Failed)
            .Select(filter => filter.ReasonCode)
            .Where(code => code is not null)
            .ToArray();
        return failedReasonCodes.Length == 0
            ? "Hard filters passed. AI evaluation is required for qualification."
            : $"Hard filters rejected the vacancy: {string.Join(", ", failedReasonCodes)}.";
    }

    private static HardFilterResult Failed(
        string ruleId,
        string reasonCode,
        string evidenceId,
        string explanation) =>
        new(ruleId, RuleOutcome.Failed, reasonCode, [evidenceId], explanation);

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

public sealed record DeterministicEvaluation(
    string RubricVersion,
    bool PassedHardFilters,
    IReadOnlyList<HardFilterResult> HardFilters,
    string Explanation);
