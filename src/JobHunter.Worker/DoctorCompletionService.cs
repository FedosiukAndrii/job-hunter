using JobHunter.AI.Abstractions;
using JobHunter.Application.Notifications;
using JobHunter.Application.Persistence;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Application.Storage;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Configuration;
using JobHunter.JobSources.JobSpy;
using JobHunter.Notifications.Telegram;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

internal sealed partial class DoctorCompletionService(
    IDatabaseMaintenance databaseMaintenance,
    IAppDataDirectory appDataDirectory,
    IOptions<StorageOptions> storageOptions,
    ICandidateProfileLoader profileLoader,
    IEnumerable<IJobSourceSubscriptionProvider> subscriptionProviders,
    IEnumerable<IJobSource> sources,
    ISourceSubscriptionStore sourceSubscriptionStore,
    TimeProvider timeProvider,
    INotificationDestinationStateStore destinationStateStore,
    IJobAnalyzer jobAnalyzer,
    IJobSpyStatusProbe jobSpyStatusProbe,
    ITelegramSetupService telegramSetupService,
    IOptions<TelegramOptions> telegramOptions,
    IHostApplicationLifetime applicationLifetime,
    ILogger<DoctorCompletionService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = appDataDirectory.GetPath(
            storageOptions.Value.DatabaseFileName);
        await databaseMaintenance.CheckIntegrityAsync(
            databasePath,
            cancellationToken);
        await profileLoader.LoadAsync(cancellationToken);

        var definitions = subscriptionProviders
            .SelectMany(provider => provider.GetSubscriptions())
            .ToArray();
        await sourceSubscriptionStore.SynchronizeAsync(
            definitions,
            timeProvider.GetUtcNow(),
            cancellationToken);

        var subscriptions = definitions
            .Where(subscription => subscription.Enabled)
            .ToArray();
        CoreChecksPassed(
            subscriptions.Length,
            telegramOptions.Value.Enabled,
            jobAnalyzer.Capabilities.IsEnabled);

        if (subscriptions.Length == 0)
        {
            NoSourcesEnabled();
        }

        var persistedSourceStates = await sourceSubscriptionStore.GetStatesAsync(
            cancellationToken);
        foreach (var subscription in subscriptions)
        {
            var persistedState = persistedSourceStates.SingleOrDefault(
                state => state.Source == subscription.Source
                    && string.Equals(
                        state.SubscriptionKey,
                        subscription.SubscriptionKey,
                        StringComparison.Ordinal));
            if (persistedState is not null
                && (!persistedState.IsEnabled
                    || persistedState.Status is
                        SourceSubscriptionStatus.Disabled
                        or SourceSubscriptionStatus.Blocked
                        or SourceSubscriptionStatus.Running))
            {
                SourceStateCheckFailed(
                    persistedState.Source.Value,
                    persistedState.SubscriptionKey,
                    persistedState.Status.ToString(),
                    persistedState.ReasonCode ?? "None");
                throw new OperatorCommandException(
                    $"Source '{persistedState.Source}/{persistedState.SubscriptionKey}' "
                    + $"failed doctor check with persisted status "
                    + $"'{persistedState.Status}' and reason "
                    + $"'{persistedState.ReasonCode ?? "None"}'.");
            }
        }

        await CheckSourceReadinessAsync(subscriptions, cancellationToken);

        var jobSpyReadiness = await jobSpyStatusProbe.CheckAsync(cancellationToken);
        if (jobSpyReadiness.Status != JobSpyReadiness.Disabled)
        {
            if (jobSpyReadiness.Status != JobSpyReadiness.Ready)
            {
                JobSpyCheckFailed(
                    jobSpyReadiness.Status.ToString(),
                    jobSpyReadiness.Diagnostic);
                throw new OperatorCommandException(
                    $"JobSpy failed doctor check with '{jobSpyReadiness.Status}': {jobSpyReadiness.Diagnostic}");
            }

            JobSpyCheckPassed(
                jobSpyReadiness.ServiceVersion ?? "unknown",
                jobSpyReadiness.ContractVersion ?? "unknown");
        }

        if (telegramOptions.Value.Enabled)
        {
            var destinationState = await destinationStateStore.GetAsync(
                telegramOptions.Value.DestinationId,
                cancellationToken);
            if (destinationState is null || !destinationState.IsEnabled)
            {
                var failureCode = destinationState?.FailureCode
                    ?? "SetupRequired";
                TelegramStateCheckFailed(failureCode);
                throw new OperatorCommandException(
                    "Telegram failed doctor check because the persisted "
                    + $"destination is disabled or missing ({failureCode}). "
                    + "Run setup-telegram to validate and enable it.");
            }

            try
            {
                var telegram = await telegramSetupService.CheckAsync(
                    cancellationToken);
                TelegramCheckPassed(telegram.BotUsername);
            }
            catch (TelegramSetupException exception)
            {
                TelegramCheckFailed(exception.Message);
                throw new OperatorCommandException(
                    $"Telegram failed doctor check: {exception.Message}");
            }
        }

        if (jobAnalyzer.Capabilities.IsEnabled)
        {
            var availability = await jobAnalyzer.CheckAvailabilityAsync(
                cancellationToken);
            if (!availability.IsAvailable)
            {
                AiCheckFailed(
                    jobAnalyzer.Capabilities.Provider,
                    availability.StatusCode);
                throw new OperatorCommandException(
                    $"AI provider '{jobAnalyzer.Capabilities.Provider}' failed doctor check with '{availability.StatusCode}'.");
            }

            AiCheckPassed(
                jobAnalyzer.Capabilities.Provider,
                availability.Model ?? "auto");
        }

        DoctorCompleted();
        applicationLifetime.StopApplication();
    }

    private async Task CheckSourceReadinessAsync(
        IReadOnlyCollection<JobSourceSubscriptionDefinition> subscriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var adapters = sources.ToDictionary(source => source.Name);
        foreach (var subscription in subscriptions)
        {
            if (subscription.Source == SourceName.LinkedInJobSpy)
            {
                continue;
            }

            if (!adapters.TryGetValue(subscription.Source, out var source))
            {
                SourceReadinessFailed(
                    subscription.Source.Value,
                    subscription.SubscriptionKey,
                    "AdapterUnavailable",
                    "No adapter is registered for the enabled source.");
                throw new OperatorCommandException(
                    $"Source '{subscription.Source}/{subscription.SubscriptionKey}' "
                    + "has no registered adapter.");
            }

            JobSourceResult result;
            try
            {
                result = await source.FetchAsync(
                    new JobSourceRequest(
                        Guid.NewGuid(),
                        subscription.Endpoint,
                        subscription.QueryId,
                        null,
                        subscription.MaximumItems),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SourceReadinessFailed(
                    subscription.Source.Value,
                    subscription.SubscriptionKey,
                    exception.GetType().Name,
                    "The source adapter threw while checking readiness.");
                throw new OperatorCommandException(
                    $"Source '{subscription.Source}/{subscription.SubscriptionKey}' "
                    + $"failed doctor readiness check with {exception.GetType().Name}.");
            }

            if (result.Status is not (
                    SourceRunStatus.Succeeded or SourceRunStatus.Partial))
            {
                var diagnostic = result.Diagnostic
                    ?? "The source did not provide a diagnostic.";
                SourceReadinessFailed(
                    subscription.Source.Value,
                    subscription.SubscriptionKey,
                    result.ErrorCode ?? result.Status.ToString(),
                    diagnostic);
                throw new OperatorCommandException(
                    $"Source '{subscription.Source}/{subscription.SubscriptionKey}' "
                    + $"failed doctor readiness check with "
                    + $"'{result.ErrorCode ?? result.Status.ToString()}': {diagnostic}");
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                var sourceStatus = result.Status.ToString();
                var observedCount = result.Jobs.Count;
                SourceReadinessPassed(
                    subscription.Source.Value,
                    subscription.SubscriptionKey,
                    sourceStatus,
                    observedCount);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 25,
        Level = LogLevel.Information,
        Message = "Doctor core checks passed: enabledSources={EnabledSourceCount}, telegramEnabled={TelegramEnabled}, aiEnabled={AiEnabled}.")]
    private partial void CoreChecksPassed(
        int enabledSourceCount,
        bool telegramEnabled,
        bool aiEnabled);

    [LoggerMessage(
        EventId = 26,
        Level = LogLevel.Warning,
        Message = "Doctor found no enabled source subscriptions.")]
    private partial void NoSourcesEnabled();

    [LoggerMessage(
        EventId = 27,
        Level = LogLevel.Information,
        Message = "Doctor AI check passed: provider={Provider}, model={Model}.")]
    private partial void AiCheckPassed(string provider, string model);

    [LoggerMessage(
        EventId = 28,
        Level = LogLevel.Error,
        Message = "Doctor AI check failed: provider={Provider}, status={StatusCode}.")]
    private partial void AiCheckFailed(string provider, string statusCode);

    [LoggerMessage(
        EventId = 29,
        Level = LogLevel.Information,
        Message = "Doctor completed successfully.")]
    private partial void DoctorCompleted();

    [LoggerMessage(
        EventId = 31,
        Level = LogLevel.Information,
        Message = "Doctor JobSpy check passed: serviceVersion={ServiceVersion}, contractVersion={ContractVersion}.")]
    private partial void JobSpyCheckPassed(
        string serviceVersion,
        string contractVersion);

    [LoggerMessage(
        EventId = 32,
        Level = LogLevel.Error,
        Message = "Doctor JobSpy check failed: status={Status}, diagnostic={Diagnostic}.")]
    private partial void JobSpyCheckFailed(string status, string diagnostic);

    [LoggerMessage(
        EventId = 33,
        Level = LogLevel.Information,
        Message = "Doctor Telegram check passed for bot={BotUsername}.")]
    private partial void TelegramCheckPassed(string botUsername);

    [LoggerMessage(
        EventId = 34,
        Level = LogLevel.Error,
        Message = "Doctor Telegram check failed: diagnostic={Diagnostic}.")]
    private partial void TelegramCheckFailed(string diagnostic);

    [LoggerMessage(
        EventId = 35,
        Level = LogLevel.Error,
        Message = "Doctor source state check failed: source={Source}, subscription={Subscription}, status={Status}, reason={Reason}.")]
    private partial void SourceStateCheckFailed(
        string source,
        string subscription,
        string status,
        string reason);

    [LoggerMessage(
        EventId = 37,
        Level = LogLevel.Information,
        Message = "Doctor source readiness check passed: source={Source}, subscription={Subscription}, status={Status}, observed={ObservedCount}.")]
    private partial void SourceReadinessPassed(
        string source,
        string subscription,
        string status,
        int observedCount);

    [LoggerMessage(
        EventId = 38,
        Level = LogLevel.Error,
        Message = "Doctor source readiness check failed: source={Source}, subscription={Subscription}, status={Status}, diagnostic={Diagnostic}.")]
    private partial void SourceReadinessFailed(
        string source,
        string subscription,
        string status,
        string diagnostic);

    [LoggerMessage(
        EventId = 36,
        Level = LogLevel.Error,
        Message = "Doctor Telegram persisted destination check failed: reason={Reason}.")]
    private partial void TelegramStateCheckFailed(string reason);
}
