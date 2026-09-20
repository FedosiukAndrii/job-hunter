using JobHunter.AI.Abstractions;

namespace JobHunter.Application.Evaluation;

public static class JobQualificationPolicy
{
    public static JobQualificationDecision Decide(
        DeterministicEvaluation deterministic,
        JobAnalysisResult? analysis,
        double minimumAiConfidence,
        int minimumAiFitScore)
    {
        ArgumentNullException.ThrowIfNull(deterministic);
        if (minimumAiConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAiConfidence));
        }

        if (minimumAiFitScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAiFitScore));
        }

        if (!deterministic.PassedHardFilters)
        {
            return CreateDecision(
                false,
                0,
                false,
                deterministic.PassedHardFilters,
                analysis);
        }

        if (analysis?.IsSuccessful != true
            || analysis.Output!.Confidence < minimumAiConfidence)
        {
            return CreateDecision(
                false,
                0,
                false,
                deterministic.PassedHardFilters,
                analysis);
        }

        return CreateDecision(
            analysis.Output.Score >= minimumAiFitScore,
            analysis.Output.Score,
            true,
            deterministic.PassedHardFilters,
            analysis);
    }

    private static JobQualificationDecision CreateDecision(
        bool qualifies,
        int score,
        bool includeAnalysisDetails,
        bool passedHardFilters,
        JobAnalysisResult? analysis) =>
        new(
            qualifies,
            Math.Clamp(score, 0, 100),
            passedHardFilters,
            includeAnalysisDetails ? TakeInsights(analysis?.Output?.Strengths) : [],
            includeAnalysisDetails ? TakeInsights(analysis?.Output?.Concerns) : [],
            includeAnalysisDetails ? analysis?.Output?.Summary : null,
            analysis?.Usage);

    private static string[] TakeInsights(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
}

public sealed record JobQualificationDecision(
    bool Qualifies,
    int Score,
    bool PassedHardFilters,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Concerns,
    string? AiSummary,
    JobAnalysisUsage? AiUsage);
