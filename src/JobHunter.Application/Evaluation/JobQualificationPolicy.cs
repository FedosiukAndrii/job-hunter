using JobHunter.AI.Abstractions;

namespace JobHunter.Application.Evaluation;

public static class JobQualificationPolicy
{
    public static JobQualificationDecision Decide(
        DeterministicEvaluation deterministic,
        JobAnalysisResult? analysis,
        double minimumAiConfidence)
    {
        ArgumentNullException.ThrowIfNull(deterministic);
        if (minimumAiConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAiConfidence));
        }

        if (!deterministic.PassedHardFilters)
        {
            return CreateDecision(
                deterministic,
                false,
                deterministic.Score.Value,
                "rules-only",
                false,
                analysis);
        }

        if (analysis?.IsSuccessful == true
            && analysis.Output!.Confidence >= minimumAiConfidence)
        {
            var combinedScore = (int)Math.Round(
                (deterministic.Score.Value + analysis.Output.Score) / 2m,
                MidpointRounding.AwayFromZero);
            return CreateDecision(
                deterministic,
                combinedScore >= deterministic.RulesAndAiThreshold,
                combinedScore,
                "rules-and-ai",
                true,
                analysis);
        }

        return CreateDecision(
            deterministic,
            deterministic.Score.Value >= deterministic.RulesOnlyThreshold,
            deterministic.Score.Value,
            "rules-only",
            false,
            analysis);
    }

    private static JobQualificationDecision CreateDecision(
        DeterministicEvaluation deterministic,
        bool qualifies,
        int score,
        string scoreMode,
        bool usedAi,
        JobAnalysisResult? analysis)
    {
        var strongest = deterministic.Criteria
            .Where(criterion => criterion.AwardedPoints > 0)
            .OrderByDescending(criterion => criterion.AwardedPoints)
            .ThenBy(criterion => criterion.CriterionId, StringComparer.Ordinal)
            .FirstOrDefault();
        var ruleStrengths = BuildRuleStrengths(deterministic.Criteria);
        var ruleConcerns = BuildRuleConcerns(deterministic.Criteria);

        return new JobQualificationDecision(
            qualifies,
            Math.Clamp(score, 0, 100),
            scoreMode,
            usedAi,
            deterministic.Score.Value,
            deterministic.PassedHardFilters,
            strongest is null
                ? null
                : $"{strongest.CriterionId} ({strongest.AwardedPoints}/{strongest.MaximumPoints})",
            deterministic.MissingData,
            TakeInsights(analysis?.Output?.Strengths, ruleStrengths),
            TakeInsights(analysis?.Output?.Concerns, ruleConcerns),
            usedAi ? analysis?.Output?.Summary : null,
            analysis?.Usage);
    }

    private static IReadOnlyList<string> TakeInsights(
        IReadOnlyList<string>? preferred,
        IReadOnlyList<string> fallback) =>
        (preferred ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray() switch
        {
            { Length: > 0 } insights => insights,
            _ => fallback
        };

    private static string[] BuildRuleStrengths(
        IReadOnlyList<CriterionEvaluation> criteria) =>
        criteria
            .Where(criterion => criterion.MaximumPoints > 0
                && criterion.AwardedPoints > 0)
            .OrderByDescending(
                criterion => criterion.AwardedPoints / (decimal)criterion.MaximumPoints)
            .ThenByDescending(criterion => criterion.AwardedPoints)
            .ThenBy(criterion => criterion.CriterionId, StringComparer.Ordinal)
            .Select(
                criterion => criterion.AwardedPoints == criterion.MaximumPoints
                    ? $"Strong {GetCriterionLabel(criterion.CriterionId)} match"
                    : $"Partial {GetCriterionLabel(criterion.CriterionId)} match")
            .Take(2)
            .ToArray();

    private static string[] BuildRuleConcerns(
        IReadOnlyList<CriterionEvaluation> criteria) =>
        criteria
            .Where(criterion => criterion.MaximumPoints > 0
                && (criterion.HasMissingData || criterion.AwardedPoints == 0))
            .OrderByDescending(criterion => criterion.HasMissingData)
            .ThenByDescending(criterion => criterion.MaximumPoints)
            .ThenBy(criterion => criterion.CriterionId, StringComparer.Ordinal)
            .Select(
                criterion => criterion.HasMissingData
                    ? GetMissingDataConcern(criterion.CriterionId)
                    : $"{GetCriterionLabel(criterion.CriterionId)} does not match preferences")
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

    private static string GetCriterionLabel(string criterionId) => criterionId switch
    {
        "coreSkills" => "core skills",
        "seniority" => "seniority",
        "relatedStack" => "related stack",
        "roleResponsibilities" => "role focus",
        "locationLanguage" => "location and language",
        "domain" => "domain",
        "compensation" => "compensation",
        _ => "configured criteria"
    };

    private static string GetMissingDataConcern(string criterionId) => criterionId switch
    {
        "coreSkills" => "Core skills are not clearly stated",
        "seniority" => "Seniority is not stated",
        "relatedStack" => "Related stack is not clearly stated",
        "roleResponsibilities" => "Role focus is not clearly stated",
        "locationLanguage" => "Location or language details are not fully stated",
        "domain" => "Preferred domain is not stated",
        "compensation" => "Salary is not stated",
        _ => "Some vacancy details are not stated"
    };
}

public sealed record JobQualificationDecision(
    bool Qualifies,
    int Score,
    string ScoreMode,
    bool UsedAi,
    int DeterministicScore,
    bool PassedHardFilters,
    string? StrongestCriterion,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Concerns,
    string? AiSummary,
    JobAnalysisUsage? AiUsage);
