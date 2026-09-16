using JobHunter.Application.Persistence;
using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Persistence;

public sealed class SqliteDatabaseMaintenance(
    IAppDataDirectory appDataDirectory,
    IOptions<StorageOptions> storageOptions)
    : IDatabaseMaintenance
{
    public async Task BackupAsync(string outputPath, CancellationToken cancellationToken)
    {
        var sourcePath = appDataDirectory.GetPath(storageOptions.Value.DatabaseFileName);
        var destinationPath = ValidateNewDestination(outputPath, sourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await CopyDatabaseAsync(sourcePath, destinationPath, cancellationToken);
        await CheckIntegrityAsync(destinationPath, cancellationToken);
    }

    public async Task RestoreAsync(
        string backupPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = ValidateExistingSource(backupPath);
        var validatedDestinationPath = ValidateNewDestination(destinationPath, sourcePath);
        await CheckIntegrityAsync(sourcePath, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(validatedDestinationPath)!);

        await CopyDatabaseAsync(sourcePath, validatedDestinationPath, cancellationToken);
        await CheckIntegrityAsync(validatedDestinationPath, cancellationToken);
    }

    public async Task<string> CheckIntegrityAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var validatedPath = ValidateExistingSource(databasePath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = validatedPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        if (results.Count != 1
            || !string.Equals(results[0], "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SQLite integrity check failed: {string.Join("; ", results)}");
        }

        return results[0];
    }

    private static async Task CopyDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string ValidateExistingSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The SQLite database file does not exist.", fullPath);
        }

        return fullPath;
    }

    private static string ValidateNewDestination(string path, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A database destination path must be absolute.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (string.Equals(fullPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A database copy destination must differ from its source.");
        }

        if (File.Exists(fullPath))
        {
            throw new IOException($"The destination database '{fullPath}' already exists.");
        }

        return fullPath;
    }
}
