using Microsoft.Extensions.Options;

namespace JobHunter.AI.Copilot.Configuration;

internal sealed class CopilotOptionsValidator : IValidateOptions<CopilotOptions>
{
    public ValidateOptionsResult Validate(string? name, CopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Model?.Trim().Length > 128)
        {
            failures.Add("AI:Copilot:Model must contain at most 128 characters.");
        }

        if (options.MaximumInputCharacters is < 10_000 or > 200_000)
        {
            failures.Add(
                "AI:Copilot:MaximumInputCharacters must be between 10000 and 200000.");
        }

        if (options.MaximumOutputCharacters is < 1_000 or > 16_000)
        {
            failures.Add(
                "AI:Copilot:MaximumOutputCharacters must be between 1000 and 16000.");
        }

        if (options.MaximumConcurrency is < 1 or > 4)
        {
            failures.Add(
                "AI:Copilot:MaximumConcurrency must be between 1 and 4.");
        }

        if (options.QueueCapacity is < 1 or > 256)
        {
            failures.Add("AI:Copilot:QueueCapacity must be between 1 and 256.");
        }

        if (options.MaximumTransientRetries is < 0 or > 2)
        {
            failures.Add(
                "AI:Copilot:MaximumTransientRetries must be between 0 and 2.");
        }

        if (options.TransientRetryDelaySeconds is < 1 or > 60)
        {
            failures.Add(
                "AI:Copilot:TransientRetryDelaySeconds must be between 1 and 60.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
