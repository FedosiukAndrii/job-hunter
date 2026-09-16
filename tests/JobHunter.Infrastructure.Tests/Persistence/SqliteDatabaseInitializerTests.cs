using System.Data.Common;
using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsyncAppliesMigrationAndRequiredPragmas()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        await using var serviceProvider = CreateServiceProvider(temporaryDirectory.Path);
        var initializer = serviceProvider.GetRequiredService<IDatabaseInitializer>();

        await initializer.InitializeAsync(CancellationToken.None);

        var databasePath = Path.Combine(temporaryDirectory.Path, "test.db");
        Assert.True(File.Exists(databasePath));

        var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Equal(
            ["20260915170000_Initial", "20260915200129_CoreModel"],
            appliedMigrations);

        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = context.Database.GetDbConnection();
            Assert.Equal(1L, await ReadInt64Async(connection, "PRAGMA foreign_keys;"));
            Assert.Equal(5000L, await ReadInt64Async(connection, "PRAGMA busy_timeout;"));
            Assert.Equal("wal", await ReadStringAsync(connection, "PRAGMA journal_mode;"));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static ServiceProvider CreateServiceProvider(string dataDirectory)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = dataDirectory,
                ["Storage:DatabaseFileName"] = "test.db",
                ["Storage:BusyTimeoutSeconds"] = "5"
            });

        var services = new ServiceCollection();
        services.AddJobHunterInfrastructure(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<long> ReadInt64Async(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadStringAsync(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
