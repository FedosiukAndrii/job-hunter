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
            return new JobQualificationDecision(
                false,
                deterministic.Score.Value,
                "rules-only",
                deterministic.Explanation,
                false);
        }

        if (analysis?.IsSuccessful == true
            && analysis.Output!.Confidence >= minimumAiConfidence)
        {
            var combinedScore = (int)Math.Round(
                (deterministic.Score.Value + analysis.Output.Score) / 2m,
                MidpointRounding.AwayFromZero);
            return new JobQualificationDecision(
                combinedScore >= deterministic.RulesAndAiThreshold,
                combinedScore,
                "rules-and-ai",
                $"{deterministic.Explanation} AI: {analysis.Output.Summary}",
                true);
        }

        return new JobQualificationDecision(
            deterministic.Score.Value >= deterministic.RulesOnlyThreshold,
            deterministic.Score.Value,
            "rules-only",
            deterministic.Explanation,
            false);
    }
}

public sealed record JobQualificationDecision(
    bool Qualifies,
    int Score,
    string ScoreMode,
    string Summary,
    bool UsedAi);
