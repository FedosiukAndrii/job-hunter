using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.Dou.Configuration;

internal sealed class DouOptionsValidator : IValidateOptions<DouOptions>
{
    public ValidateOptionsResult Validate(string? name, DouOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Uri.TryCreate(options.FeedUrl, UriKind.Absolute, out var feedUri)
            || feedUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(feedUri.IdnHost, "jobs.dou.ua", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(feedUri.UserInfo))
        {
            failures.Add("Sources:Dou:FeedUrl must be an HTTPS URL on jobs.dou.ua.");
        }

        if (options.DefaultIntervalMinutes is < 10 or > 1440)
        {
            failures.Add("Sources:Dou:DefaultIntervalMinutes must be between 10 and 1440.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 120)
        {
            failures.Add("Sources:Dou:RequestTimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaximumResponseBytes is < 16 * 1024 or > 10 * 1024 * 1024)
        {
            failures.Add("Sources:Dou:MaximumResponseBytes must be between 16384 and 10485760.");
        }

        if (options.MaximumItems is < 1 or > 1000)
        {
            failures.Add("Sources:Dou:MaximumItems must be between 1 and 1000.");
        }

        if (options.MaximumDescriptionCharacters is < 1000 or > 200_000)
        {
            failures.Add(
                "Sources:Dou:MaximumDescriptionCharacters must be between 1000 and 200000.");
        }

        if (options.DetailMinimumIntervalSeconds is < 1 or > 60)
        {
            failures.Add(
                "Sources:Dou:DetailMinimumIntervalSeconds must be between 1 and 60.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
