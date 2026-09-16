using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.JobSpy.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.JobSpy;

internal sealed class JobSpySourceSubscriptionProvider(IOptions<JobSpyOptions> options)
    : IJobSourceSubscriptionProvider
{
    public IReadOnlyList<JobSourceSubscriptionDefinition> GetSubscriptions() =>
        [
            new JobSourceSubscriptionDefinition(
                SourceName.LinkedInJobSpy,
                "primary",
                new Uri(options.Value.Endpoint, UriKind.Absolute),
                options.Value.SearchTerm,
                options.Value.MaximumResults,
                TimeSpan.FromMinutes(options.Value.MinimumIntervalMinutes),
                options.Value.Enabled)
        ];
}
