using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using JobHunter.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.IntegrationTests;

public sealed class CopilotAvailabilityValidationServiceTests
{
    [Fact]
    public async Task StopsStartupWhenStrictModeCannotUseConfiguredModel()
    {
        var analyzer = new StubAnalyzer(
            isEnabled: true,
            new JobAnalyzerAvailability(
                false,
                "CopilotModelUnavailable",
                "gpt-5.6-luna"));
            var service = CreateService(analyzer);

        var exception = await Assert.ThrowsAsync<CopilotModelAvailabilityException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("gpt-5.6-luna", exception.Message, StringComparison.Ordinal);
        Assert.Contains("CopilotModelUnavailable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, analyzer.AvailabilityCheckCount);
    }

    [Fact]
    public async Task StopsStartupWhenAnalyzerIsDisabled()
    {
        var analyzer = new StubAnalyzer(
            isEnabled: false,
            new JobAnalyzerAvailability(
                false,
                "NotUsed",
                null));
        var service = CreateService(analyzer);

        var exception = await Assert.ThrowsAsync<CopilotModelAvailabilityException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("AiDisabled", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, analyzer.AvailabilityCheckCount);
    }

    private static CopilotAvailabilityValidationService CreateService(StubAnalyzer analyzer) =>
        new(
            analyzer,
            Options.Create(
                new CopilotOptions
                {
                    Model = "gpt-5.6-luna"
                }),
            NullLogger<CopilotAvailabilityValidationService>.Instance);

    private sealed class StubAnalyzer(
        bool isEnabled,
        JobAnalyzerAvailability availability)
        : IJobAnalyzer
    {
        public int AvailabilityCheckCount { get; private set; }

        public JobAnalyzerCapabilities Capabilities { get; } = new(
            "copilot",
            isEnabled,
            true,
            48_000,
            4_000);

        public Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AvailabilityCheckCount++;
            return Task.FromResult(availability);
        }

        public Task<JobAnalysisResult> AnalyzeAsync(
            JobAnalysisRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
