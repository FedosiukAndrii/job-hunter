namespace JobHunter.Infrastructure.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(bool create = true)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "JobHunter.Tests",
            Guid.NewGuid().ToString("N"));

        if (create)
        {
            Directory.CreateDirectory(Path);
        }
    }

    public string Path { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
