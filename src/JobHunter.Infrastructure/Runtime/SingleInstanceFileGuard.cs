using System.Text;
using JobHunter.Application.Runtime;
using JobHunter.Application.Storage;

namespace JobHunter.Infrastructure.Runtime;

public sealed class SingleInstanceFileGuard(IAppDataDirectory appDataDirectory)
    : IApplicationInstanceGuard
{
    private const string LockFileName = "job-hunter.lock";

    public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockPath = appDataDirectory.GetPath(LockFileName);
        FileStream lockStream;

        try
        {
            lockStream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 256,
                FileOptions.WriteThrough);
        }
        catch (IOException exception)
        {
            throw new ApplicationInstanceUnavailableException(
                "Unable to acquire the Job Hunter instance lock. Another instance may already be using this data directory.",
                exception);
        }

        try
        {
            var owner = Encoding.UTF8.GetBytes(
                $"{Environment.ProcessId}:{DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            lockStream.SetLength(0);
            lockStream.Write(owner);
            lockStream.Flush(flushToDisk: true);
        }
        catch
        {
            lockStream.Dispose();
            throw;
        }

        return ValueTask.FromResult<IAsyncDisposable>(new FileInstanceLease(lockStream));
    }

    private sealed class FileInstanceLease(FileStream lockStream) : IAsyncDisposable
    {
        private FileStream? _lockStream = lockStream;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _lockStream, null)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
