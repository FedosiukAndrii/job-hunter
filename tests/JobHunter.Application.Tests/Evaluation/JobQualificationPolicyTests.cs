using JobHunter.AI.Abstractions;
using JobHunter.Application.Evaluation;

namespace JobHunter.Application.Tests.Evaluation;

public sealed class JobQualificationPolicyTests
{
    [Fact]
    public void DecideUsesAiScoreAsTheQualificationScore()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(),
            CreateAnalysis(score: 80, confidence: 0.8),
            0.65,
            75);

        Assert.True(decision.Qualifies);
        Assert.Equal(80, decision.Score);
        Assert.Equal("AI summary.", decision.AiSummary);
        Assert.Contains("Strong .NET/backend match", decision.Strengths);
        Assert.Contains("AWS is not stated", decision.Concerns);
        Assert.Equal(20, decision.AiUsage?.InputTokens);
    }

    [Fact]
    public void DecideDefersWhenAiConfidenceIsBelowMinimum()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(),
            CreateAnalysis(score: 100, confidence: 0.64),
            0.65,
            75);

        Assert.False(decision.Qualifies);
        Assert.Equal(0, decision.Score);
        Assert.Null(decision.AiSummary);
        Assert.Equal(20, decision.AiUsage?.InputTokens);
    }

    [Fact]
    public void DecideRejectsWhenAiScoreIsBelowThreshold()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(),
            CreateAnalysis(score: 74, confidence: 0.9),
            0.65,
            75);

        Assert.False(decision.Qualifies);
        Assert.Equal(74, decision.Score);
    }

    [Fact]
    public void DecideRejectsBeforeAiWhenHardFilterFails()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(passedHardFilters: false),
            CreateAnalysis(score: 100, confidence: 0.9),
            0.65,
            75);

        Assert.False(decision.Qualifies);
    }

    private static DeterministicEvaluation CreateDeterministic(
        bool passedHardFilters = true) =>
        new(
            DeterministicJobEvaluator.RubricVersion,
            passedHardFilters,
            [],
            "Hard-filter summary.");

    private static JobAnalysisResult CreateAnalysis(int score, double confidence) =>
        new(
            JobAnalysisStatus.Succeeded,
            "copilot",
            "test-model",
            JobAnalysisSchema.Version,
            AiEvaluationPolicy.Version,
            new JobAnalysisOutput(
                score,
                confidence,
                "AI summary.",
                [],
                ["Strong .NET/backend match"],
                ["AWS is not stated"]),
            new JobAnalysisUsage(100, 100, 20, 10, null),
            [],
            null);
}
