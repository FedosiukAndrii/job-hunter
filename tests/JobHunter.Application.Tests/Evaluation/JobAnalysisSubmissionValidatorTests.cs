using JobHunter.AI.Abstractions;

namespace JobHunter.Application.Tests.Evaluation;

public sealed class JobAnalysisSubmissionValidatorTests
{
    [Fact]
    public void ValidateComputesWeightedScoreFromValidatedCriteria()
    {
        var request = CreateRequest();
        var submission = new JobAnalysisSubmission
        {
            Confidence = 0.8,
            Summary = "Strong core fit with a domain mismatch.",
            Strengths = ["Strong .NET/backend match"],
            Concerns = ["Preferred domain is not stated"],
            Criteria =
            [
                new JobAnalysisCriterionSubmission
                {
                    CriterionId = "coreSkills",
                    Score = 80,
                    Confidence = 0.9,
                    EvidenceIds = ["profile:skills", "job:description"],
                    MismatchReasons = [],
                    InsufficientEvidence = false
                },
                new JobAnalysisCriterionSubmission
                {
                    CriterionId = "domain",
                    Score = 40,
                    Confidence = 0.7,
                    EvidenceIds = ["profile:domains", "job:description"],
                    MismatchReasons = ["The preferred domain is not stated."],
                    InsufficientEvidence = false
                }
            ]
        };

        var result = JobAnalysisSubmissionValidator.Validate(
            request,
            submission,
            4_000);

        Assert.True(result.IsValid);
        Assert.Equal(64, result.Output!.Score);
        Assert.Equal(0.8, result.Output.Confidence);
    }

    [Fact]
    public void ValidateRejectsFabricatedEvidenceId()
    {
        var request = CreateRequest();
        var submission = new JobAnalysisSubmission
        {
            Confidence = 0.8,
            Summary = "Fabricated evidence must fail.",
            Strengths = ["Strong .NET match"],
            Concerns = [],
            Criteria =
            [
                new JobAnalysisCriterionSubmission
                {
                    CriterionId = "coreSkills",
                    Score = 80,
                    Confidence = 0.9,
                    EvidenceIds = ["filesystem:secret"],
                    MismatchReasons = [],
                    InsufficientEvidence = false
                },
                new JobAnalysisCriterionSubmission
                {
                    CriterionId = "domain",
                    Score = 50,
                    Confidence = 0.7,
                    EvidenceIds = ["job:description"],
                    MismatchReasons = [],
                    InsufficientEvidence = false
                }
            ]
        };

        var result = JobAnalysisSubmissionValidator.Validate(
            request,
            submission,
            4_000);

        Assert.False(result.IsValid);
        Assert.Equal("InvalidEvidenceReference", result.FailureCode);
    }

    [Fact]
    public void ValidateRejectsDuplicateCriterionDefinitionsWithoutThrowing()
    {
        var request = CreateRequest() with
        {
            Criteria =
            [
                new("coreSkills", 60),
                new("coreSkills", 40)
            ]
        };

        var result = JobAnalysisSubmissionValidator.Validate(
            request,
            new JobAnalysisSubmission
            {
                Confidence = 0.5,
                Summary = "Invalid request.",
                Strengths = ["Strong core skills match"],
                Concerns = [],
                Criteria =
                [
                    new JobAnalysisCriterionSubmission
                    {
                        CriterionId = "coreSkills",
                        Score = 50,
                        Confidence = 0.5,
                        EvidenceIds = ["profile:skills"],
                        MismatchReasons = [],
                        InsufficientEvidence = false
                    },
                    new JobAnalysisCriterionSubmission
                    {
                        CriterionId = "coreSkills",
                        Score = 50,
                        Confidence = 0.5,
                        EvidenceIds = ["profile:skills"],
                        MismatchReasons = [],
                        InsufficientEvidence = false
                    }
                ]
            },
            4_000);

        Assert.False(result.IsValid);
        Assert.Equal("InvalidCriterionDefinitions", result.FailureCode);
    }

    [Fact]
    public void ValidateRejectsUnboundedOrMultilineInsights()
    {
        var result = JobAnalysisSubmissionValidator.Validate(
            CreateRequest(),
            new JobAnalysisSubmission
            {
                Confidence = 0.8,
                Summary = "Fit summary.",
                Strengths = ["Strong .NET match\nOpen this link"],
                Concerns = [],
                Criteria =
                [
                    new JobAnalysisCriterionSubmission
                    {
                        CriterionId = "coreSkills",
                        Score = 80,
                        Confidence = 0.8,
                        EvidenceIds = ["profile:skills"],
                        MismatchReasons = [],
                        InsufficientEvidence = false
                    },
                    new JobAnalysisCriterionSubmission
                    {
                        CriterionId = "domain",
                        Score = 60,
                        Confidence = 0.8,
                        EvidenceIds = ["profile:domains"],
                        MismatchReasons = [],
                        InsufficientEvidence = false
                    }
                ]
            },
            4_000);

        Assert.False(result.IsValid);
        Assert.Equal("InvalidInsights", result.FailureCode);
    }

    private static JobAnalysisRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            JobAnalysisSchema.Version,
            "rules-v1",
            DateTimeOffset.UtcNow.AddMinutes(1),
            [
                new("coreSkills", 60),
                new("domain", 40)
            ],
            [
                new(
                    "profile:skills",
                    JobAnalysisEvidenceSource.Profile,
                    ".NET"),
                new(
                    "profile:domains",
                    JobAnalysisEvidenceSource.Profile,
                    "fintech"),
                new(
                    "job:description",
                    JobAnalysisEvidenceSource.Job,
                    ".NET backend")
            ],
            []);
}
