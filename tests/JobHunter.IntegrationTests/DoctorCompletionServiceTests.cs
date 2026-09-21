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
using JobHunter.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.IntegrationTests;

public sealed class DoctorCompletionServiceTests
{
    [Fact]
    public async Task StartChecksEnabledExternalDependenciesWithoutSendingTelegramMessage()
    {
        var database = new StubDatabaseMaintenance();
        var analyzer = new StubJobAnalyzer(
            new JobAnalyzerAvailability(true, "Ready", "fixture-model"));
        var jobSpy = new StubJobSpyStatusProbe(
            new JobSpyReadinessResult(
                JobSpyReadiness.Ready,
                "Ready.",
                "0.1.0",
                "v1"));
        var telegram = new StubTelegramSetupService();
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            database,
            analyzer,
            jobSpy,
            telegram,
            lifetime,
            telegramEnabled: true);

        await service.StartAsync(CancellationToken.None);

        Assert.EndsWith(
            Path.Combine("data", "doctor.db"),
            database.CheckedPath,
            StringComparison.Ordinal);
        Assert.Equal(1, analyzer.AvailabilityCheckCount);
        Assert.Equal(1, jobSpy.CheckCount);
        Assert.Equal(1, telegram.CheckCount);
        Assert.Equal(0, telegram.ValidateCount);
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task StartFailsWhenPrimarySourceReadinessFails()
    {
        var source = new StubJobSource(
            JobSourceResult.Failed(
                "FixtureUnavailable",
                "The fixture source is unavailable."));
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            new StubJobSpyStatusProbe(
                new JobSpyReadinessResult(
                    JobSpyReadiness.Disabled,
                    "Disabled.",
                    null,
                    null)),
            new StubTelegramSetupService(),
            new StubHostApplicationLifetime(),
            telegramEnabled: false,
            source: source);

        var exception = await Assert.ThrowsAsync<OperatorCommandException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("FixtureUnavailable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, source.FetchCount);
    }

    [Fact]
    public async Task StartFailsWhenEnabledJobSpyIsUnavailable()
    {
        var jobSpy = new StubJobSpyStatusProbe(
            new JobSpyReadinessResult(
                JobSpyReadiness.Unavailable,
                "JobSpy is unavailable.",
                null,
                null));
        var telegram = new StubTelegramSetupService();
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            jobSpy,
            telegram,
            lifetime,
            telegramEnabled: true);

        var exception = await Assert.ThrowsAsync<OperatorCommandException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("JobSpy", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, telegram.CheckCount);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task StartFailsWhenEnabledTelegramDestinationIsInvalid()
    {
        var telegram = new StubTelegramSetupService
        {
            CheckException = new TelegramSetupException(
                "The configured Telegram destination must be a private chat.")
        };
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            new StubJobSpyStatusProbe(
                new JobSpyReadinessResult(
                    JobSpyReadiness.Disabled,
                    "Disabled.",
                    null,
                    null)),
            telegram,
            lifetime,
            telegramEnabled: true);

        var exception = await Assert.ThrowsAsync<OperatorCommandException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("Telegram", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, telegram.CheckCount);
        Assert.Equal(0, telegram.ValidateCount);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task StartFailsWhenConfiguredSourceIsPersistedAsBlocked()
    {
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            new StubJobSpyStatusProbe(
                new JobSpyReadinessResult(
                    JobSpyReadiness.Disabled,
                    "Disabled.",
                    null,
                    null)),
            new StubTelegramSetupService(),
            lifetime,
            telegramEnabled: false,
            sourceState: new PersistedSourceSubscriptionState(
                SourceName.Dou,
                "doctor",
                true,
                SourceSubscriptionStatus.Blocked,
                "FixtureBlocked",
                "Fixture source is blocked."));

        var exception = await Assert.ThrowsAsync<OperatorCommandException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("Blocked", exception.Message, StringComparison.Ordinal);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task StartSynchronizesAConfigurationDisabledSourceBeforeCheckingItsState()
    {
        var subscriptionStore = new StubSourceSubscriptionStore(
            [
                new PersistedSourceSubscriptionState(
                    SourceName.Dou,
                    "doctor",
                    false,
                    SourceSubscriptionStatus.Disabled,
                    "ConfigurationDisabled",
                    null)
            ]);
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            new StubJobSpyStatusProbe(
                new JobSpyReadinessResult(
                    JobSpyReadiness.Disabled,
                    "Disabled.",
                    null,
                    null)),
            new StubTelegramSetupService(),
            lifetime,
            telegramEnabled: false,
            subscriptionStore: subscriptionStore);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, subscriptionStore.SynchronizeCount);
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task StartFailsBeforeTelegramRequestWhenDestinationIsDisabled()
    {
        var telegram = new StubTelegramSetupService();
        var lifetime = new StubHostApplicationLifetime();
        var service = CreateService(
            new StubDatabaseMaintenance(),
            new StubJobAnalyzer(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model")),
            new StubJobSpyStatusProbe(
                new JobSpyReadinessResult(
                    JobSpyReadiness.Disabled,
                    "Disabled.",
                    null,
                    null)),
            telegram,
            lifetime,
            telegramEnabled: true,
            destinationState: new NotificationDestinationStateSnapshot(
                "telegram-primary",
                false,
                "TelegramHttp403",
                DateTimeOffset.UnixEpoch));

        var exception = await Assert.ThrowsAsync<OperatorCommandException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("setup-telegram", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, telegram.CheckCount);
        Assert.False(lifetime.StopRequested);
    }

    private static DoctorCompletionService CreateService(
        IDatabaseMaintenance database,
        IJobAnalyzer analyzer,
        IJobSpyStatusProbe jobSpy,
        ITelegramSetupService telegram,
        IHostApplicationLifetime lifetime,
        bool telegramEnabled,
        PersistedSourceSubscriptionState? sourceState = null,
        NotificationDestinationStateSnapshot? destinationState = null,
        IJobSource? source = null,
        StubSourceSubscriptionStore? subscriptionStore = null) =>
        new(
            database,
            new StubAppDataDirectory(),
            Options.Create(
                new StorageOptions
                {
                    DatabaseFileName = "doctor.db"
                }),
            new StubProfileLoader(),
            [new StubSubscriptionProvider()],
            [source ?? new StubJobSource()],
            subscriptionStore ?? new StubSourceSubscriptionStore(
                sourceState is null ? [] : [sourceState]),
            TimeProvider.System,
            new StubDestinationStateStore(
                destinationState
                    ?? (telegramEnabled
                        ? new NotificationDestinationStateSnapshot(
                            "telegram-primary",
                            true,
                            null,
                            DateTimeOffset.UnixEpoch)
                        : null)),
            analyzer,
            jobSpy,
            telegram,
            Options.Create(
                new TelegramOptions
                {
                    Enabled = telegramEnabled
                }),
            lifetime,
            NullLogger<DoctorCompletionService>.Instance);

    private sealed class StubSourceSubscriptionStore(
        IReadOnlyList<PersistedSourceSubscriptionState> initialStates)
        : ISourceSubscriptionStore
    {
        private IReadOnlyList<PersistedSourceSubscriptionState> states = initialStates;

        public int SynchronizeCount { get; private set; }

        public Task SynchronizeAsync(
            IReadOnlyCollection<JobSourceSubscriptionDefinition> definitions,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SynchronizeCount++;
            states = states
                .Select(state =>
                {
                    var definition = definitions.SingleOrDefault(candidate =>
                        candidate.Source == state.Source
                        && string.Equals(
                            candidate.SubscriptionKey,
                            state.SubscriptionKey,
                            StringComparison.Ordinal));
                    return definition is { Enabled: true } && !state.IsEnabled
                        ? state with
                        {
                            IsEnabled = true,
                            Status = SourceSubscriptionStatus.Enabled,
                            ReasonCode = null,
                            Diagnostic = null
                        }
                        : state;
                })
                .ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DueJobSourceSubscription>> GetDueAsync(
            DateTimeOffset now,
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DueJobSourceSubscription>> GetRunnableAsync(
            SourceName source,
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PersistedSourceSubscriptionState>> GetStatesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(states);
        }
    }

    private sealed class StubDestinationStateStore(
        NotificationDestinationStateSnapshot? state)
        : INotificationDestinationStateStore
    {
        public Task<NotificationDestinationStateSnapshot?> GetAsync(
            string destinationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(state);
        }

        public Task SetDestinationEnabledAsync(
            string destinationId,
            bool enabled,
            string? failureCode,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubDatabaseMaintenance : IDatabaseMaintenance
    {
        public string? CheckedPath { get; private set; }

        public Task BackupAsync(
            string outputPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreAsync(
            string backupPath,
            string destinationPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> CheckIntegrityAsync(
            string databasePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckedPath = databasePath;
            return Task.FromResult("ok");
        }
    }

    private sealed class StubAppDataDirectory : IAppDataDirectory
    {
        public string RootPath { get; } = Path.Combine("root", "data");

        public string GetPath(string relativePath) =>
            Path.Combine(RootPath, relativePath);
    }

    private sealed class StubProfileLoader : ICandidateProfileLoader
    {
        public Task<LoadedCandidateProfile> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new LoadedCandidateProfile(
                    new CandidateProfile(),
                    "{}",
                    "sha256:profile",
                    "profile.yaml",
                    null));
        }
    }

    private sealed class StubSubscriptionProvider : IJobSourceSubscriptionProvider
    {
        public IReadOnlyList<JobSourceSubscriptionDefinition> GetSubscriptions() =>
            [
                new(
                    SourceName.Dou,
                    "doctor",
                    new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                    ".NET",
                    10,
                    TimeSpan.FromMinutes(12),
                    true)
            ];
    }

    private sealed class StubJobSource(JobSourceResult? result = null) : IJobSource
    {
        public SourceName Name => SourceName.Dou;

        public int FetchCount { get; private set; }

        public Task<JobSourceResult> FetchAsync(
            JobSourceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FetchCount++;
            return Task.FromResult(
                result ?? JobSourceResult.Succeeded([], null));
        }
    }

    private sealed class StubJobAnalyzer(JobAnalyzerAvailability availability)
        : IJobAnalyzer
    {
        public JobAnalyzerCapabilities Capabilities { get; } =
            new("test-ai", true, true, 48_000, 4_000);

        public int AvailabilityCheckCount { get; private set; }

        public Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AvailabilityCheckCount++;
            return Task.FromResult(availability);
        }

        public Task<JobAnalysisResult> AnalyzeAsync(
            JobAnalysisRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubJobSpyStatusProbe(JobSpyReadinessResult result)
        : IJobSpyStatusProbe
    {
        public int CheckCount { get; private set; }

        public Task<JobSpyReadinessResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubTelegramSetupService : ITelegramSetupService
    {
        public TelegramSetupException? CheckException { get; init; }

        public int CheckCount { get; private set; }

        public int ValidateCount { get; private set; }

        public Task<TelegramSetupResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCount++;
            if (CheckException is not null)
            {
                throw CheckException;
            }

            return Task.FromResult(
                new TelegramSetupResult("job_hunter_test_bot"));
        }

        public Task<TelegramSetupResult> ValidateAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCount++;
            return Task.FromResult(
                new TelegramSetupResult("job_hunter_test_bot"));
        }
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public bool StopRequested { get; private set; }

        public void StopApplication() => StopRequested = true;
    }
}
