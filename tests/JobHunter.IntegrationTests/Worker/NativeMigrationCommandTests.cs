using System.Diagnostics;

namespace JobHunter.IntegrationTests.Worker;

public sealed class NativeMigrationCommandTests
{
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
            var workerAssemblyPath = GetWorkerAssemblyPath();
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(workerAssemblyPath);
            startInfo.ArgumentList.Add("migrate");
            startInfo.ArgumentList.Add("--Storage:DataDirectory");
            startInfo.ArgumentList.Add(dataDirectory);
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
                throw new TimeoutException("The Worker migrate command did not exit within 30 seconds.");
            }

            var output = await standardOutput;
            var error = await standardError;

            Assert.True(
                process.ExitCode == 0,
                $"Worker exited with code {process.ExitCode}.{Environment.NewLine}{error}{Environment.NewLine}{output}");
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
}
