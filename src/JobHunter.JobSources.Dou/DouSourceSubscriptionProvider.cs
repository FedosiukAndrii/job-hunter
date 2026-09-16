using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.JobSources.Dou.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.Dou;

internal sealed class DouSourceSubscriptionProvider(IOptions<DouOptions> options)
    : IJobSourceSubscriptionProvider
{
    public IReadOnlyList<JobSourceSubscriptionDefinition> GetSubscriptions() =>
        [
            new JobSourceSubscriptionDefinition(
                SourceName.Dou,
                "primary",
                new Uri(options.Value.FeedUrl, UriKind.Absolute),
                null,
                options.Value.MaximumItems,
                TimeSpan.FromMinutes(options.Value.DefaultIntervalMinutes),
                options.Value.Enabled)
        ];
}
