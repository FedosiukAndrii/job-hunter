using JobHunter.Application.Sources;
using JobHunter.JobSources.JobSpy;
using JobHunter.JobSources.JobSpy.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.ContractTests.JobSpy;

public sealed class JobSpyRegistrationTests
{
    [Fact]
    public void AddJobSpySourceDoesNotExposeDisabledSourceForExecution()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Sources:LinkedInJobSpy:Enabled"] = "false",
                ["Sources:LinkedInJobSpy:ExperimentalAcknowledged"] = "false",
                ["Sources:LinkedInJobSpy:Endpoint"] = "http://127.0.0.1:8080/"
            });
        var services = new ServiceCollection();

        services.AddJobSpySource(configuration);
        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        Assert.Empty(serviceProvider.GetServices<IJobSource>());
        Assert.NotNull(serviceProvider.GetRequiredService<IJobSpyStatusProbe>());
    }
}
