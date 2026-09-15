using System.Data.Common;
using JobHunter.Application.Persistence;
using JobHunter.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer(
    IDbContextFactory<JobHunterDbContext> contextFactory,
    IOptions<StorageOptions> storageOptions)
    : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = context.Database.GetDbConnection();
            await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                $"PRAGMA busy_timeout={storageOptions.Value.BusyTimeoutSeconds * 1000};",
                cancellationToken);

            var journalMode = await ExecuteScalarAsync(
                connection,
                "PRAGMA journal_mode=WAL;",
                cancellationToken);

            if (!string.Equals(journalMode?.ToString(), "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite did not enable WAL journal mode.");
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
