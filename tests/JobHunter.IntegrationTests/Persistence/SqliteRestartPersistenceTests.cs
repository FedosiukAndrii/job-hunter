using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.IntegrationTests.Persistence;

public sealed class SqliteRestartPersistenceTests
{
    [Fact]
    public async Task InitializeAsyncAfterServiceRestartReusesExistingDatabase()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "JobHunter.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        try
        {
            await InitializeDatabaseAsync(dataDirectory);
            await InitializeDatabaseAsync(dataDirectory);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dataDirectory, "restart.db"),
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";

            var migrationCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(7L, migrationCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static async Task InitializeDatabaseAsync(string dataDirectory)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = dataDirectory,
                ["Storage:DatabaseFileName"] = "restart.db"
            });

        var services = new ServiceCollection();
        services.AddJobHunterInfrastructure(configuration);

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var initializer = serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
    }
}
