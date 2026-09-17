using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot;
using JobHunter.AI.Copilot.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.Copilot;

public sealed class CopilotJobAnalyzerTests
{
    [Fact]
    public async Task AnalyzeMapsValidatedSessionOutcome()
    {
        var runner = new StubSessionRunner(
            (_, _, _) => Task.FromResult(
                new CopilotRunOutcome(
                    JobAnalysisStatus.Succeeded,
                    new JobAnalysisOutput(82, 0.8, "Strong fit.", []),
                    "fixture-model",
                    100,
                    30,
                    200,
                    null)));
        using var analyzer = CreateAnalyzer(runner);

        var result = await analyzer.AnalyzeAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(82, result.Output!.Score);
        Assert.Equal("fixture-model", result.Model);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(30, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task AnalyzeRejectsWhenBoundedQueueIsFull()
    {
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new StubSessionRunner(
            async (_, _, cancellationToken) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new CopilotRunOutcome(
                    JobAnalysisStatus.Succeeded,
                    new JobAnalysisOutput(80, 0.8, "Fit.", []),
                    "fixture-model",
                    100,
                    20,
                    100,
                    null);
            });
        using var analyzer = CreateAnalyzer(
            runner,
            new CopilotOptions
            {
                MaximumConcurrency = 1,
                QueueCapacity = 1,
                MaximumTransientRetries = 0
            });

        var first = analyzer.AnalyzeAsync(CreateRequest(), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = analyzer.AnalyzeAsync(CreateRequest(), CancellationToken.None);
        await Task.Delay(50);

        var rejected = await analyzer.AnalyzeAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(JobAnalysisStatus.OverBudget, rejected.Status);
        Assert.Equal("AiQueueFull", rejected.FailureCode);
        release.TrySetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task AnalyzeRetriesOnceWithCorrectivePromptAfterInvalidStructuredOutput()
    {
        var prompts = new List<string>();
        var runner = new StubSessionRunner(
            (_, prompt, _) =>
            {
                prompts.Add(prompt);
                return Task.FromResult(
                    prompts.Count == 1
                        ? CopilotRunOutcome.Failure(
                            JobAnalysisStatus.InvalidOutput,
                            "InvalidCriterionScore",
                            "fixture-model",
                            100,
                            20,
                            50,
                            0.01)
                        : new CopilotRunOutcome(
                            JobAnalysisStatus.Succeeded,
                            new JobAnalysisOutput(82, 0.8, "Strong fit.", []),
                            "fixture-model",
                            120,
                            30,
                            60,
                            null,
                            0.02));
            });
        using var analyzer = CreateAnalyzer(runner);

        var result = await analyzer.AnalyzeAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(2, prompts.Count);
        Assert.Contains(
            "Every criteria[].score must be a whole integer from 0 through 100",
            prompts[1],
            StringComparison.Ordinal);
        Assert.Equal(220, result.Usage.InputTokens);
        Assert.Equal(50, result.Usage.OutputTokens);
        Assert.Equal(110, result.Usage.OutputCharacters);
        Assert.Equal(0.03, result.Usage.AiCredits);
        Assert.Equal(2, result.Usage.RequestCount);
    }

    private static CopilotJobAnalyzer CreateAnalyzer(
        ICopilotSessionRunner runner,
        CopilotOptions? options = null) =>
        new(runner, Options.Create(options ?? new CopilotOptions()));

    private static JobAnalysisRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            JobAnalysisSchema.Version,
            "rules-v1",
            DateTimeOffset.UtcNow.AddMinutes(1),
            [new("coreSkills", 100)],
            [
                new(
                    "job:description",
                    JobAnalysisEvidenceSource.Job,
                    ".NET backend role")
            ],
            []);

    private sealed class StubSessionRunner(
        Func<
            JobAnalysisRequest,
            string,
            CancellationToken,
            Task<CopilotRunOutcome>> run)
        : ICopilotSessionRunner
    {
        public Task<CopilotAvailabilityOutcome> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new CopilotAvailabilityOutcome(
                    true,
                    "Ready",
                    "fixture-model"));
        }

        public Task<CopilotRunOutcome> RunAsync(
            JobAnalysisRequest request,
            string prompt,
            CancellationToken cancellationToken) =>
            run(request, prompt, cancellationToken);
    }
}
