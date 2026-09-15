using JobHunter.Application.Runtime;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Runtime;
using JobHunter.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Runtime;

public sealed class SingleInstanceFileGuardTests
{
    [Fact]
    public async Task AcquireAsyncPreventsConcurrentLeaseAndAllowsReacquisition()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var appDataDirectory = new PlatformAppDataDirectory(
            Options.Create(new StorageOptions { DataDirectory = temporaryDirectory.Path }));
        var guard = new SingleInstanceFileGuard(appDataDirectory);

        var firstLease = await guard.AcquireAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<ApplicationInstanceUnavailableException>(
                async () => await guard.AcquireAsync(CancellationToken.None));
        }
        finally
        {
            await firstLease.DisposeAsync();
        }

        await using var reacquiredLease = await guard.AcquireAsync(CancellationToken.None);
    }
}
