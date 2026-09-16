namespace JobHunter.AI.Abstractions;

public sealed class NullJobAnalyzer : IJobAnalyzer
{
    public JobAnalyzerCapabilities Capabilities { get; } =
        new("disabled", false, true, 0, 0);

    public Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new JobAnalyzerAvailability(false, "AiDisabled", null));
    }

    public Task<JobAnalysisResult> AnalyzeAsync(
        JobAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            JobAnalysisResult.Failure(
                JobAnalysisStatus.Disabled,
                Capabilities.Provider,
                null,
                request.SchemaVersion,
                request.RubricVersion,
                "AiDisabled"));
    }
}
