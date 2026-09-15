using JobHunter.Application.Runtime;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.Worker;
using Microsoft.Extensions.Options;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!WorkerCommand.TryParse(args, out var command))
        {
            Console.Error.WriteLine("Usage: job-hunter [run|migrate] [configuration options]");
            return 64;
        }

        if (command.Kind == WorkerCommandKind.Help)
        {
            Console.WriteLine("Usage: job-hunter [run|migrate] [configuration options]");
            return 0;
        }

        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = command.ConfigurationArguments,
                ContentRootPath = AppContext.BaseDirectory
            });
        builder.Services
            .AddOptions<WorkerOptions>()
            .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
            .Validate(
                options => options.SchedulerTickSeconds is >= 1 and <= 3600,
                "Worker:SchedulerTickSeconds must be between 1 and 3600.")
            .Validate(
                options => options.ShutdownTimeoutSeconds is >= 1 and <= 300,
                "Worker:ShutdownTimeoutSeconds must be between 1 and 300.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<HostOptions>()
            .Configure<IOptions<WorkerOptions>>(
                (hostOptions, workerOptions) =>
                    hostOptions.ShutdownTimeout =
                        TimeSpan.FromSeconds(workerOptions.Value.ShutdownTimeoutSeconds));

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddJobHunterInfrastructure(builder.Configuration);
        builder.Services.AddHostedService<StartupInitializationService>();

        if (command.Kind == WorkerCommandKind.Migrate)
        {
            builder.Services.AddHostedService<MigrationCompletionService>();
        }
        else
        {
            builder.Services.AddHostedService<Worker>();
        }

        try
        {
            using var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        catch (ApplicationInstanceUnavailableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (OptionsValidationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
    }
}
