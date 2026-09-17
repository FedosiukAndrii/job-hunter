using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

internal sealed partial class CopilotAvailabilityValidationService(
    IJobAnalyzer jobAnalyzer,
    IOptions<CopilotOptions> copilotOptions,
    ILogger<CopilotAvailabilityValidationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!jobAnalyzer.Capabilities.IsEnabled)
        {
            return;
        }

        var availability = await jobAnalyzer.CheckAvailabilityAsync(cancellationToken);
        if (availability.IsAvailable)
        {
            ModelCheckPassed(
                jobAnalyzer.Capabilities.Provider,
                availability.Model ?? ConfiguredModel);
            return;
        }

        ModelCheckFailed(
            jobAnalyzer.Capabilities.Provider,
            availability.Model ?? ConfiguredModel,
            availability.StatusCode,
            copilotOptions.Value.FailStartupWhenModelUnavailable);
        if (copilotOptions.Value.FailStartupWhenModelUnavailable)
        {
            throw new CopilotModelAvailabilityException(
                jobAnalyzer.Capabilities.Provider,
                availability.Model ?? ConfiguredModel,
                availability.StatusCode);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string ConfiguredModel => string.IsNullOrWhiteSpace(copilotOptions.Value.Model)
        ? "auto"
        : copilotOptions.Value.Model.Trim();

    [LoggerMessage(
        EventId = 39,
        Level = LogLevel.Information,
        Message = "Copilot startup model check passed: provider={Provider}, model={Model}.")]
    private partial void ModelCheckPassed(string provider, string model);

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Warning,
        Message = "Copilot startup model check failed: provider={Provider}, model={Model}, status={StatusCode}, failStartup={FailStartup}.")]
    private partial void ModelCheckFailed(
        string provider,
        string model,
        string statusCode,
        bool failStartup);
}

internal sealed class CopilotModelAvailabilityException(
    string provider,
    string model,
    string statusCode)
    : Exception(
        $"AI provider '{provider}' cannot start with model '{model}' ({statusCode}). "
        + "Select a supported model explicitly, for example AI:Copilot:Model=auto, "
        + "or set AI:Copilot:FailStartupWhenModelUnavailable=false to use rules-only fallback.")
{
}
