using System.Globalization;
using JobHunter.Application.Persistence;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.Infrastructure.Persistence;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.IntegrationTests.Sources;

public sealed class DouFixtureIngestionTests
{
    [Fact]
    public async Task FixtureImportIsIdempotentAcrossRepeatedPersistence()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "JobHunter.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:DataDirectory"] = dataDirectory,
                    ["Storage:DatabaseFileName"] = "dou-import.db"
                });
            var services = new ServiceCollection();
            services.AddJobHunterInfrastructure(configuration);
            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await serviceProvider
                .GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(CancellationToken.None);

            var subscriptionId = await AddSubscriptionAsync(serviceProvider);
            var run = await serviceProvider
                .GetRequiredService<ISourceRunStore>()
                .TryStartAsync(
                    subscriptionId,
                    At("2026-09-15T10:00:00Z"),
                    TimeSpan.FromMinutes(5),
                    CancellationToken.None);
            Assert.NotNull(run);

            var fixture = File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "Dou",
                    "standard-dotnet.xml"));
            var parsed = new DouRssParser().Parse(
                fixture,
                ".NET",
                At("2026-09-15T10:00:00Z"),
                new DouOptions());
            var store = serviceProvider.GetRequiredService<IJobIngestionStore>();

            await store.PersistAsync(
                run.SourceRunId,
                subscriptionId,
                ".NET",
                parsed.Records,
                CancellationToken.None);
            await store.PersistAsync(
                run.SourceRunId,
                subscriptionId,
                ".NET",
                parsed.Records,
                CancellationToken.None);

            var contextFactory =
                serviceProvider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();
            Assert.Equal(1, await context.Jobs.CountAsync());
            Assert.Equal(1, await context.JobObservations.CountAsync());
            Assert.Equal(0, await context.JobRevisions.CountAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static async Task<Guid> AddSubscriptionAsync(IServiceProvider serviceProvider)
    {
        var contextFactory =
            serviceProvider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var subscription = SourceSubscription.Create(
            SourceName.Dou,
            "dotnet",
            "{}",
            TimeSpan.FromMinutes(12),
            enabled: true,
            At("2026-09-15T09:00:00Z"));
        context.SourceSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        return subscription.Id;
    }

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
