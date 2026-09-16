using JobHunter.Application.Runtime;
using JobHunter.Application.Profiles;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.JobSources.Dou.DependencyInjection;
using JobHunter.JobSources.JobSpy.DependencyInjection;
using JobHunter.Worker;
using Microsoft.Extensions.Options;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!WorkerCommand.TryParse(args, out var command))
        {
            PrintUsage(Console.Error);
            return 64;
        }

        if (command.Kind == WorkerCommandKind.Help)
        {
            PrintUsage(Console.Out);
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
        builder.Services.AddSingleton(command);
        builder.Services.AddJobHunterInfrastructure(builder.Configuration);
        builder.Services.AddDouJobSource(builder.Configuration);
        builder.Services.AddJobSpySource(builder.Configuration);
        builder.Services.AddHostedService<StartupInitializationService>();

        if (command.Kind is WorkerCommandKind.Backup
            or WorkerCommandKind.Restore
            or WorkerCommandKind.IntegrityCheck)
        {
            builder.Services.AddHostedService<DatabaseMaintenanceCompletionService>();
        }
        else if (command.Kind == WorkerCommandKind.Migrate)
        {
            builder.Services.AddHostedService<MigrationCompletionService>();
        }
        else
        {
            builder.Services.AddHostedService<CandidateProfileInitializationService>();
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
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }
        catch (CandidateProfileValidationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 5;
        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  job-hunter run [configuration options]");
        writer.WriteLine("  job-hunter migrate [configuration options]");
        writer.WriteLine("  job-hunter backup --output <absolute-path> [configuration options]");
        writer.WriteLine(
            "  job-hunter restore --input <backup-path> --output <new-database-path> [configuration options]");
        writer.WriteLine(
            "  job-hunter integrity-check [--input <database-path>] [configuration options]");
    }
}
