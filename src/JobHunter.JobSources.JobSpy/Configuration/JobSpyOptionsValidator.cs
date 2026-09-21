using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.JobSpy.Configuration;

public sealed class JobSpyOptionsValidator : IValidateOptions<JobSpyOptions>
{
    public ValidateOptionsResult Validate(string? name, JobSpyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Enabled && !options.ExperimentalAcknowledged)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:ExperimentalAcknowledged must be true when the experimental source is enabled.");
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || !endpoint.IsLoopback
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            failures.Add(
                "Sources:LinkedInJobSpy:Endpoint must be an absolute loopback HTTP or HTTPS URL without credentials.");
        }

        if (options.Enabled
            && (string.IsNullOrWhiteSpace(options.SearchTerm)
                || options.SearchTerm.Trim().Length > 256))
        {
            failures.Add(
                "Sources:LinkedInJobSpy:SearchTerm must contain between 1 and 256 characters when the source is enabled.");
        }

        if (!string.IsNullOrWhiteSpace(options.Location)
            && options.Location.Trim().Length > 256)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:Location must contain at most 256 characters when supplied.");
        }

        if (!string.IsNullOrWhiteSpace(options.Location)
            && options.Location.Any(character =>
                char.IsControl(character) && !char.IsWhiteSpace(character)))
        {
            failures.Add(
                "Sources:LinkedInJobSpy:Location must not contain non-whitespace control characters.");
        }

        if (options.MinimumIntervalMinutes is < 60 or > 10080)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:MinimumIntervalMinutes must be between 60 and 10080.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 300)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:RequestTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaximumResults is < 1 or > 50)
        {
            failures.Add("Sources:LinkedInJobSpy:MaximumResults must be between 1 and 50.");
        }

        if (options.MaximumResponseBytes is < 16 * 1024 or > 10 * 1024 * 1024)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:MaximumResponseBytes must be between 16384 and 10485760.");
        }

        if (options.BlockedBackoffHours is < 1 or > 720)
        {
            failures.Add(
                "Sources:LinkedInJobSpy:BlockedBackoffHours must be between 1 and 720.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
