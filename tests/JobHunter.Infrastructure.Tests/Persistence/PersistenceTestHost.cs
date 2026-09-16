using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

internal sealed class PersistenceTestHost : IAsyncDisposable
{
    private PersistenceTestHost(
        TemporaryDirectory directory,
        ServiceProvider services)
    {
        Directory = directory;
        Services = services;
    }

    public TemporaryDirectory Directory { get; }

    public ServiceProvider Services { get; }

    public string DatabasePath => Path.Combine(Directory.Path, "test.db");

    public static async Task<PersistenceTestHost> CreateAsync()
    {
        var directory = new TemporaryDirectory();
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = directory.Path,
                ["Storage:DatabaseFileName"] = "test.db",
                ["Storage:BusyTimeoutSeconds"] = "5"
            });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddJobHunterInfrastructure(configuration);
        var services = serviceCollection.BuildServiceProvider(validateScopes: true);

        try
        {
            await services
                .GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(CancellationToken.None);
            return new PersistenceTestHost(directory, services);
        }
        catch
        {
            await services.DisposeAsync();
            directory.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Directory.Dispose();
    }
}
