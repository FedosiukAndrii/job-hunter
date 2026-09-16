using Microsoft.Extensions.Options;

namespace JobHunter.AI.Copilot.Configuration;

internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider)
            || options.Provider.Trim().Length > 64)
        {
            failures.Add("AI:Provider must contain between 1 and 64 characters.");
        }
        else if (options.Enabled
            && !string.Equals(
                options.Provider.Trim(),
                CopilotOptions.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"AI provider '{options.Provider.Trim()}' is not registered.");
        }

        if (options.AnalysisTimeoutSeconds is < 10 or > 300)
        {
            failures.Add(
                "AI:AnalysisTimeoutSeconds must be between 10 and 300.");
        }

        if (options.MinimumConfidence is < 0 or > 1)
        {
            failures.Add("AI:MinimumConfidence must be between 0 and 1.");
        }

        if (options.TransientFailureRetryMinutes is < 1 or > 1440)
        {
            failures.Add(
                "AI:TransientFailureRetryMinutes must be between 1 and 1440.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
