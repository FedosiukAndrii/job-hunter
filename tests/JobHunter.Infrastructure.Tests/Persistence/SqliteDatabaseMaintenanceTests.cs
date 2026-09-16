using JobHunter.Application.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class SqliteDatabaseMaintenanceTests
{
    [Fact]
    public async Task BackupRestoresToSeparateDatabaseAndPassesIntegrityCheck()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var maintenance = host.Services.GetRequiredService<IDatabaseMaintenance>();
        var backupPath = Path.Combine(host.Directory.Path, "backups", "backup.db");
        var restoredPath = Path.Combine(host.Directory.Path, "restored", "restored.db");

        await maintenance.BackupAsync(backupPath, CancellationToken.None);
        await maintenance.RestoreAsync(backupPath, restoredPath, CancellationToken.None);
        var integrity = await maintenance.CheckIntegrityAsync(
            restoredPath,
            CancellationToken.None);

        Assert.Equal("ok", integrity);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = restoredPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";

        Assert.Equal(
            5L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }
}
