using JobHunter.AI.Copilot.Configuration;
using JobHunter.AI.Copilot.DependencyInjection;
using JobHunter.Application.Runtime;
using JobHunter.Application.Profiles;
using JobHunter.Application.Orchestration;
using JobHunter.Application.Notifications;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.JobSources.Dou.DependencyInjection;
using JobHunter.JobSources.JobSpy.DependencyInjection;
using JobHunter.Notifications.Telegram;
using JobHunter.Notifications.Telegram.DependencyInjection;
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
        ConfigureLogging(builder.Logging);
        builder.Services
            .AddOptions<WorkerOptions>()
            .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
            .Validate(
                options => options.SchedulerTickSeconds is >= 1 and <= 3600,
                "Worker:SchedulerTickSeconds must be between 1 and 3600.")
            .Validate(
                options => options.ShutdownTimeoutSeconds is >= 1 and <= 300,
                "Worker:ShutdownTimeoutSeconds must be between 1 and 300.")
            .Validate(
                options => options.MaximumConcurrentSources is >= 1 and <= 16,
                "Worker:MaximumConcurrentSources must be between 1 and 16.")
            .Validate(
                options => options.MaximumSubscriptionsPerTick is >= 1 and <= 1000,
                "Worker:MaximumSubscriptionsPerTick must be between 1 and 1000.")
            .Validate(
                options => options.SourceLeaseSeconds is >= 5 and <= 3600,
                "Worker:SourceLeaseSeconds must be between 5 and 3600.")
            .Validate(
                options => options.NotificationDispatchIntervalSeconds is >= 1 and <= 60,
                "Worker:NotificationDispatchIntervalSeconds must be between 1 and 60.")
            .Validate(
                options => options.NotificationBatchSize is >= 1 and <= 1000,
                "Worker:NotificationBatchSize must be between 1 and 1000.")
            .Validate(
                options => options.NotificationLeaseSeconds is >= 5 and <= 1800,
                "Worker:NotificationLeaseSeconds must be between 5 and 1800.")
            .Validate(
                options => options.NotificationMaximumAttempts is >= 1 and <= 20,
                "Worker:NotificationMaximumAttempts must be between 1 and 20.")
            .Validate(
                options => options.NotificationMaximumRetrySeconds is >= 60 and <= 86400,
                "Worker:NotificationMaximumRetrySeconds must be between 60 and 86400.")
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
        builder.Services.AddCopilotJobAnalysis(builder.Configuration);
        builder.Services.AddDouJobSource(builder.Configuration);
        builder.Services.AddJobSpySource(builder.Configuration);
        builder.Services.AddTelegramNotifications(builder.Configuration);
        builder.Services.AddSingleton(
            serviceProvider =>
            {
                var workerOptions =
                    serviceProvider.GetRequiredService<IOptions<WorkerOptions>>().Value;
                var aiOptions =
                    serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                return new ScanOrchestratorOptions
                {
                    MaximumConcurrentSources = workerOptions.MaximumConcurrentSources,
                    MaximumSubscriptionsPerTick = workerOptions.MaximumSubscriptionsPerTick,
                    SourceLeaseDuration =
                        TimeSpan.FromSeconds(workerOptions.SourceLeaseSeconds),
                    AiAnalysisTimeout =
                        TimeSpan.FromSeconds(aiOptions.AnalysisTimeoutSeconds),
                    MinimumAiConfidence = aiOptions.MinimumConfidence,
                    MinimumAiFitScore = aiOptions.MinimumFitScore,
                    AiTransientFailureRetryDelay =
                        TimeSpan.FromMinutes(
                            aiOptions.TransientFailureRetryMinutes)
                };
            });
        builder.Services.AddSingleton<ScanOrchestrator>();
        builder.Services.AddSingleton(
            serviceProvider =>
            {
                var workerOptions =
                    serviceProvider.GetRequiredService<IOptions<WorkerOptions>>().Value;
                return new NotificationOutboxDispatcherOptions
                {
                    MaximumBatchSize = workerOptions.NotificationBatchSize,
                    LeaseDuration =
                        TimeSpan.FromSeconds(workerOptions.NotificationLeaseSeconds),
                    MaximumAttempts = workerOptions.NotificationMaximumAttempts,
                    MaximumRetryDelay =
                        TimeSpan.FromSeconds(workerOptions.NotificationMaximumRetrySeconds)
                };
            });
        builder.Services.AddSingleton<NotificationOutboxDispatcher>();
        if (command.Kind != WorkerCommandKind.ShowProfile)
        {
            builder.Services.AddHostedService<StartupInitializationService>();
        }

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
        else if (command.Kind == WorkerCommandKind.SetupTelegram)
        {
            builder.Services.AddHostedService<TelegramSetupCompletionService>();
        }
        else if (command.Kind == WorkerCommandKind.ShowProfile)
        {
            builder.Services.AddHostedService<ProfilePreviewCompletionService>();
        }
        else if (command.Kind == WorkerCommandKind.RunOnce)
        {
            builder.Services.AddHostedService<CopilotAvailabilityValidationService>();
            builder.Services.AddHostedService<RunOnceCompletionService>();
        }
        else if (command.Kind == WorkerCommandKind.Doctor)
        {
            builder.Services.AddHostedService<DoctorCompletionService>();
        }
        else
        {
            builder.Services.AddHostedService<CopilotAvailabilityValidationService>();
            builder.Services.AddHostedService<CandidateProfileInitializationService>();
            builder.Services.AddHostedService<Worker>();
            if (builder.Configuration.GetValue<bool>("Retention:Enabled"))
            {
                builder.Services.AddHostedService<RetentionWorker>();
            }
            if (builder.Configuration.GetValue<bool>("Telegram:Enabled"))
            {
                builder.Services.AddHostedService<NotificationDispatchWorker>();
            }
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
        catch (TelegramSetupException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 6;
        }
        catch (OperatorCommandException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 7;
        }
        catch (CopilotModelAvailabilityException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 8;
        }
    }

    internal static void ConfigureLogging(ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        // The foreground worker must report source and operator failures through
        // its process output. The default Windows Event Log provider can itself
        // fail for a standard, non-elevated user and hide the original error.
        logging.ClearProviders();
        logging.AddSimpleConsole(options => options.SingleLine = true);
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  job-hunter run [configuration options]");
        writer.WriteLine(
            "  job-hunter run-once --source <dou|linkedin-jobspy> [configuration options]");
        writer.WriteLine("  job-hunter doctor [configuration options]");
        writer.WriteLine("  job-hunter migrate [configuration options]");
        writer.WriteLine("  job-hunter backup --output <absolute-path> [configuration options]");
        writer.WriteLine(
            "  job-hunter restore --input <backup-path> --output <new-database-path> [configuration options]");
        writer.WriteLine(
            "  job-hunter integrity-check [--input <database-path>] [configuration options]");
        writer.WriteLine("  job-hunter setup-telegram [configuration options]");
        writer.WriteLine("  job-hunter show-profile [configuration options]");
    }
}
