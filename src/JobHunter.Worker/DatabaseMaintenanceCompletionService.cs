using JobHunter.Application.Persistence;
using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

internal sealed partial class DatabaseMaintenanceCompletionService(
    WorkerCommand command,
    IDatabaseMaintenance databaseMaintenance,
    IAppDataDirectory appDataDirectory,
    IOptions<StorageOptions> storageOptions,
    IHostApplicationLifetime applicationLifetime,
    ILogger<DatabaseMaintenanceCompletionService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        switch (command.Kind)
        {
            case WorkerCommandKind.Backup:
                await databaseMaintenance.BackupAsync(command.OutputPath!, cancellationToken);
                MaintenanceCompleted("backup", command.OutputPath!);
                break;
            case WorkerCommandKind.Restore:
                await databaseMaintenance.RestoreAsync(
                    command.InputPath!,
                    command.OutputPath!,
                    cancellationToken);
                MaintenanceCompleted("restore", command.OutputPath!);
                break;
            case WorkerCommandKind.IntegrityCheck:
                var databasePath = command.InputPath
                    ?? appDataDirectory.GetPath(storageOptions.Value.DatabaseFileName);
                await databaseMaintenance.CheckIntegrityAsync(databasePath, cancellationToken);
                MaintenanceCompleted("integrity-check", databasePath);
                break;
            default:
                throw new InvalidOperationException(
                    $"Command '{command.Kind}' is not a database maintenance operation.");
        }

        applicationLifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Information,
        Message = "Database {Operation} completed successfully for {DatabasePath}.")]
    private partial void MaintenanceCompleted(string operation, string databasePath);
}
