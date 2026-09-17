using JobHunter.AI.Abstractions;
using JobHunter.Application.Evaluation;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Tests.Evaluation;

public sealed class JobQualificationPolicyTests
{
    [Fact]
    public void DecideUsesEqualRulesAndAiWeight()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(score: 70, rulesAndAiThreshold: 75),
            CreateAnalysis(score: 90, confidence: 0.8),
            0.65);

        Assert.True(decision.Qualifies);
        Assert.Equal(80, decision.Score);
        Assert.Equal("rules-and-ai", decision.ScoreMode);
        Assert.True(decision.UsedAi);
        Assert.Equal("AI summary.", decision.AiSummary);
        Assert.Equal(70, decision.DeterministicScore);
        Assert.Contains("Strong .NET/backend match", decision.Strengths);
        Assert.Contains("AWS is not stated", decision.Concerns);
        Assert.Equal(20, decision.AiUsage?.InputTokens);
    }

    [Fact]
    public void DecideFallsBackToRulesOnlyBelowMinimumConfidence()
    {
        var decision = JobQualificationPolicy.Decide(
            CreateDeterministic(
                score: 73,
                rulesOnlyThreshold: 72,
                rulesAndAiThreshold: 90),
            CreateAnalysis(score: 100, confidence: 0.64),
            0.65);

        Assert.True(decision.Qualifies);
        Assert.Equal(73, decision.Score);
        Assert.Equal("rules-only", decision.ScoreMode);
        Assert.False(decision.UsedAi);
        Assert.Null(decision.AiSummary);
        Assert.Equal(20, decision.AiUsage?.InputTokens);
    }

    private static DeterministicEvaluation CreateDeterministic(
        int score,
        int rulesOnlyThreshold = 72,
        int rulesAndAiThreshold = 75) =>
        new(
            "rules-v1",
            true,
            new JobScore(score),
            rulesOnlyThreshold,
            rulesAndAiThreshold,
            [],
            [],
            [],
            "Rules summary.");

    private static JobAnalysisResult CreateAnalysis(int score, double confidence) =>
        new(
            JobAnalysisStatus.Succeeded,
            "copilot",
            "test-model",
            JobAnalysisSchema.Version,
            "rules-v1",
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
