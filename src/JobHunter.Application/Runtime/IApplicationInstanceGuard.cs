namespace JobHunter.Application.Runtime;

public interface IApplicationInstanceGuard
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}
