using JobHunter.Application.Persistence;
using JobHunter.Application.Runtime;

namespace JobHunter.Worker;

internal sealed partial class StartupInitializationService(
    IApplicationInstanceGuard instanceGuard,
    IDatabaseInitializer databaseInitializer,
    ILogger<StartupInitializationService> logger)
    : IHostedService
{
    private IAsyncDisposable? _instanceLease;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _instanceLease = await instanceGuard.AcquireAsync(cancellationToken);
        var initialized = false;

        try
        {
            await databaseInitializer.InitializeAsync(cancellationToken);
            initialized = true;
            InitializationCompleted();
        }
        finally
        {
            if (!initialized)
            {
                await ReleaseLeaseAsync();
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        ReleaseLeaseAsync().AsTask();

    private async ValueTask ReleaseLeaseAsync()
    {
        var lease = Interlocked.Exchange(ref _instanceLease, null);
        if (lease is not null)
        {
            await lease.DisposeAsync();
        }
    }

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Application data and SQLite initialization completed.")]
    private partial void InitializationCompleted();
}
