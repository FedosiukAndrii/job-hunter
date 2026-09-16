using System.Diagnostics;

namespace JobHunter.IntegrationTests.Worker;

public sealed class NativeMigrationCommandTests
{
    [Fact]
    public async Task HelpCommandPrintsUsageWithoutStartingHost()
    {
        var result = await RunWorkerAsync(
            Path.GetTempPath(),
            "--help");

        Assert.Contains("Usage:", result.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task MigrateCommandDoesNotDependOnCurrentDirectory()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "JobHunter.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(testRoot, "working");
        var dataDirectory = Path.Combine(testRoot, "data");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            await RunWorkerAsync(
                workingDirectory,
                "migrate",
                "--Storage:DataDirectory",
                dataDirectory);
            Assert.True(File.Exists(Path.Combine(dataDirectory, "job-hunter.db")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DatabaseMaintenanceCommandsCreateAndValidateIndependentCopies()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "JobHunter.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(testRoot, "working");
        var dataDirectory = Path.Combine(testRoot, "data");
        var backupPath = Path.Combine(testRoot, "backup", "job-hunter.db");
        var restoredPath = Path.Combine(testRoot, "restored", "job-hunter.db");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var storageArguments = new[]
            {
                "--Storage:DataDirectory",
                dataDirectory
            };
            await RunWorkerAsync(workingDirectory, ["migrate", .. storageArguments]);
            await RunWorkerAsync(
                workingDirectory,
                ["backup", "--output", backupPath, .. storageArguments]);
            await RunWorkerAsync(
                workingDirectory,
                ["integrity-check", "--input", backupPath, .. storageArguments]);
            await RunWorkerAsync(
                workingDirectory,
                [
                    "restore",
                    "--input",
                    backupPath,
                    "--output",
                    restoredPath,
                    .. storageArguments
                ]);

            Assert.True(File.Exists(backupPath));
            Assert.True(File.Exists(restoredPath));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static async Task<WorkerProcessResult> RunWorkerAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var workerAssemblyPath = GetWorkerAssemblyPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(workerAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Worker process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"The Worker {arguments[0]} command did not exit within 30 seconds.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"Worker exited with code {process.ExitCode}.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        return new WorkerProcessResult(output, error);
    }

    private static string GetWorkerAssemblyPath()
    {
        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Unable to determine the build configuration.");
        var targetFramework = testOutputDirectory.Name;

        for (var current = testOutputDirectory; current is not null; current = current.Parent)
        {
            if (!File.Exists(Path.Combine(current.FullName, "JobHunter.slnx")))
            {
                continue;
            }

            var workerAssemblyPath = Path.Combine(
                current.FullName,
                "src",
                "JobHunter.Worker",
                "bin",
                configuration,
                targetFramework,
                "JobHunter.Worker.dll");

            if (!File.Exists(workerAssemblyPath))
            {
                throw new FileNotFoundException(
                    "The Worker build output was not found.",
                    workerAssemblyPath);
            }

            return workerAssemblyPath;
        }

        throw new InvalidOperationException("Unable to locate the repository root.");
    }

    private sealed record WorkerProcessResult(
        string StandardOutput,
        string StandardError);
}
