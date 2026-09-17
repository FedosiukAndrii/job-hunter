using JobHunter.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobHunter.IntegrationTests;

public sealed class WorkerLoggingTests
{
    [Fact]
    public void ConfigureLoggingUsesConsoleWithoutWindowsEventLog()
    {
        var builder = Host.CreateApplicationBuilder();

        ProgramEntry.ConfigureLogging(builder.Logging);

        using var host = builder.Build();
        var providerNames = host.Services
            .GetServices<ILoggerProvider>()
            .Select(provider => provider.GetType().FullName)
            .ToArray();

        Assert.Contains(
            "Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider",
            providerNames);
        Assert.DoesNotContain(
            providerNames,
            name => name?.Contains("EventLog", StringComparison.Ordinal) == true);
    }
}
