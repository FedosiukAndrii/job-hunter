using System.Globalization;
using JobHunter.AI.Abstractions;
using JobHunter.Application.Notifications;
using JobHunter.Application.Orchestration;
using JobHunter.Application.Persistence;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.DependencyInjection;
using JobHunter.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.IntegrationTests.Orchestration;

public sealed class RulesOnlyPipelineTests
{
    [Fact]
    public async Task RepeatedPollPersistsOneJobEvaluationAndNotificationIntent()
    {
        await using var host = await PipelineTestHost.CreateAsync();

        var first = await host.Orchestrator.RunDueAsync(CancellationToken.None);
        var dispatch = await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        host.Time.Advance(TimeSpan.FromMinutes(13));
        var second = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, first.SucceededCount);
        Assert.Equal(1, first.ObservedCount);
        Assert.Equal(1, first.NotificationIntentCount);
        Assert.Equal(1, dispatch.SentCount);
        Assert.Equal(1, host.NotificationChannel.SendCount);
        Assert.Equal(1, second.SucceededCount);
        Assert.Equal(0, second.NotificationIntentCount);

        await using var context = await host.CreateDbContextAsync();
        Assert.Equal(1, await context.Jobs.CountAsync());
        Assert.Equal(1, await context.RuleEvaluations.CountAsync());
        Assert.Equal(1, await context.NotificationOutbox.CountAsync());
        Assert.Equal(2, await context.JobObservations.CountAsync());
        Assert.Equal(2, await context.SourceRuns.CountAsync());
    }

    [Fact]
    public async Task ChangedJobIsReevaluatedWithoutDuplicateNotification()
    {
        var fetchCount = 0;
        await using var host = await PipelineTestHost.CreateAsync(
            (record, _) =>
            {
                var current = Interlocked.Increment(ref fetchCount) == 1
                    ? record
                    : record with
                    {
                        DescriptionHtml = "<p>.NET C# backend role with Azure</p>",
                        DescriptionText = ".NET C# backend role with Azure",
                        ContentHash = "sha256:content-v2",
                        RawPayloadHash = "sha256:payload-v2"
                    };
                return Task.FromResult(
                    JobSourceResult.Succeeded([current], null));
            });

        var first = await host.Orchestrator.RunDueAsync(CancellationToken.None);
        var dispatch = await host.Dispatcher.DispatchDueAsync(CancellationToken.None);
        host.Time.Advance(TimeSpan.FromMinutes(13));
        var second = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, first.NotificationIntentCount);
        Assert.Equal(1, dispatch.SentCount);
        Assert.Equal(0, second.NotificationIntentCount);
        Assert.Equal(1, host.NotificationChannel.SendCount);

        await using var context = await host.CreateDbContextAsync();
        Assert.Equal(1, await context.Jobs.CountAsync());
        Assert.Equal(1, await context.JobRevisions.CountAsync());
        Assert.Equal(1, await context.NotificationOutbox.CountAsync());
    }

    [Fact]
    public async Task ConcurrentTicksDoNotFetchOneSubscriptionTwice()
    {
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await PipelineTestHost.CreateAsync(
            async (record, cancellationToken) =>
            {
                fetchStarted.TrySetResult();
                await releaseFetch.Task.WaitAsync(cancellationToken);
                return JobSourceResult.Succeeded([record], null);
            });

        var firstTick = host.Orchestrator.RunDueAsync(CancellationToken.None);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTick = await host.Orchestrator.RunDueAsync(CancellationToken.None);
        releaseFetch.TrySetResult();
        var firstSummary = await firstTick;

        Assert.Equal(1, firstSummary.StartedCount);
        Assert.Equal(0, secondTick.StartedCount);
        Assert.Equal(1, host.Source.FetchCount);
    }

    [Fact]
    public async Task LongRunningFetchKeepsSourceRunLeaseAlive()
    {
        await using var host = await PipelineTestHost.CreateAsync(
            async (record, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
                return JobSourceResult.Succeeded([record], null);
            },
            timeProvider: TimeProvider.System,
            sourceLeaseDuration: TimeSpan.FromSeconds(5));

        var summary = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.SucceededCount);
        Assert.Equal(1, summary.NotificationIntentCount);
        await using var context = await host.CreateDbContextAsync();
        var run = await context.SourceRuns.SingleAsync();
        Assert.Equal(SourceRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task SourceLeaseStaysAliveWhileAwaitingFinalPersistence()
    {
        var persistenceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptions = new JobSourceSubscriptionDefinition[]
        {
            new(
                SourceName.Dou,
                "hold-persistence-gate",
                new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                "hold-persistence-gate",
                10,
                TimeSpan.FromMinutes(12),
                true),
            new(
                SourceName.Dou,
                "await-final-persistence",
                new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                "await-final-persistence",
                10,
                TimeSpan.FromMinutes(12),
                true)
        };
        await using var host = await PipelineTestHost.CreateAsync(
            timeProvider: TimeProvider.System,
            sourceLeaseDuration: TimeSpan.FromSeconds(5),
            contextualFetch: async (request, record, cancellationToken) =>
            {
                if (request.QueryId == "hold-persistence-gate")
                {
                    return JobSourceResult.Succeeded([record], null);
                }

                await persistenceStarted.Task.WaitAsync(cancellationToken);
                return JobSourceResult.Failed(
                    "ExpectedFixtureFailure",
                    "The fixture run waits for final persistence.");
            },
            subscriptions: subscriptions,
            ingestionStoreFactory: serviceProvider =>
                new DelayedIngestionStore(
                    new EfJobIngestionStore(
                        serviceProvider.GetRequiredService<
                            IDbContextFactory<JobHunterDbContext>>()),
                    "hold-persistence-gate",
                    persistenceStarted,
                    TimeSpan.FromSeconds(7)));

        var summary = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.SucceededCount);
        Assert.Equal(1, summary.FailedCount);
        await using var context = await host.CreateDbContextAsync();
        var runs = await context.SourceRuns.ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.DoesNotContain(runs, run => run.Status == SourceRunStatus.Running);
        Assert.Contains(runs, run => run.Status == SourceRunStatus.Succeeded);
        Assert.Contains(
            runs,
            run => run.Status == SourceRunStatus.Failed
                && run.ErrorCode == "ExpectedFixtureFailure");
    }

    [Fact]
    public async Task CancelledScanReleasesSourceRunLease()
    {
        var fetchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await PipelineTestHost.CreateAsync(
            async (_, cancellationToken) =>
            {
                fetchStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using var cancellation = new CancellationTokenSource();

        var runTask = host.Orchestrator.RunDueAsync(cancellation.Token);
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        await using var context = await host.CreateDbContextAsync();
        var run = await context.SourceRuns.SingleAsync();
        var subscription = await context.SourceSubscriptions.SingleAsync();
        Assert.Equal(SourceRunStatus.Failed, run.Status);
        Assert.Equal("RunCancelled", run.ErrorCode);
        Assert.Equal(SourceSubscriptionStatus.Enabled, subscription.Status);
    }

    [Fact]
    public async Task PartialRunDoesNotChangeExistingJobLifecycle()
    {
        var returnPartial = false;
        await using var host = await PipelineTestHost.CreateAsync(
            (record, _) => Task.FromResult(
                returnPartial
                    ? new JobSourceResult(
                        SourceRunStatus.Partial,
                        [],
                        null,
                        false,
                        null,
                        null,
                        "PartialFixture",
                        "A fixture omitted one item.")
                    : JobSourceResult.Succeeded([record], null)));

        await host.Orchestrator.RunDueAsync(CancellationToken.None);
        returnPartial = true;
        host.Time.Advance(TimeSpan.FromMinutes(13));
        var partial = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, partial.PartialCount);
        await using var context = await host.CreateDbContextAsync();
        var job = await context.Jobs.SingleAsync();
        Assert.Equal(JobLifecycle.Active, job.Lifecycle);
    }

    [Fact]
    public async Task RunOnceIgnoresScheduleButNotBlockedState()
    {
        var blockNext = false;
        await using var host = await PipelineTestHost.CreateAsync(
            (record, _) => Task.FromResult(
                blockNext
                    ? JobSourceResult.Blocked(
                        "FixtureBlocked",
                        "The fixture source is blocked.")
                    : JobSourceResult.Succeeded([record], null)));

        await host.Orchestrator.RunDueAsync(CancellationToken.None);
        var immediate = await host.Orchestrator.RunOnceAsync(
            SourceName.Dou,
            CancellationToken.None);
        blockNext = true;
        host.Time.Advance(TimeSpan.FromMinutes(13));
        await host.Orchestrator.RunDueAsync(CancellationToken.None);
        var blocked = await host.Orchestrator.RunOnceAsync(
            SourceName.Dou,
            CancellationToken.None);

        Assert.Equal(1, immediate.StartedCount);
        Assert.Equal(0, blocked.StartedCount);
        Assert.Equal(3, host.Source.FetchCount);
    }

    [Fact]
    public async Task AcceptedAiAnalysisUsesCombinedThresholdAndIsReused()
    {
        var analyzer = new StubJobAnalyzer(
            request => new JobAnalysisResult(
                JobAnalysisStatus.Succeeded,
                "test-ai",
                "fixture-model",
                request.SchemaVersion,
                request.RubricVersion,
                new JobAnalysisOutput(40, 0.9, "Weak semantic fit.", []),
                new JobAnalysisUsage(500, 100, 80, 20, null),
                [],
                null));
        await using var host = await PipelineTestHost.CreateAsync(analyzer: analyzer);

        var first = await host.Orchestrator.RunDueAsync(CancellationToken.None);
        host.Time.Advance(TimeSpan.FromMinutes(13));
        var second = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, first.AcceptedAiAnalysisCount);
        Assert.Equal(0, first.NotificationIntentCount);
        Assert.Equal(0, second.NotificationIntentCount);
        Assert.Equal(1, analyzer.CallCount);
        await using var context = await host.CreateDbContextAsync();
        Assert.Equal(1, await context.AiAnalyses.CountAsync());
    }

    [Fact]
    public async Task AiFailureIsPersistedAndFallsBackWithoutFailingSourceRun()
    {
        var analyzer = new StubJobAnalyzer(
            request => JobAnalysisResult.Failure(
                JobAnalysisStatus.TransientFailure,
                "test-ai",
                null,
                request.SchemaVersion,
                request.RubricVersion,
                "FixtureUnavailable"));
        await using var host = await PipelineTestHost.CreateAsync(analyzer: analyzer);

        var summary = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        await using var context = await host.CreateDbContextAsync();
        var sourceRun = await context.SourceRuns.SingleAsync();
        Assert.True(
            summary.SucceededCount == 1,
            $"{sourceRun.ErrorCode}: {sourceRun.Diagnostic}");
        Assert.Equal(1, summary.AiFallbackCount);
        Assert.Equal(1, summary.NotificationIntentCount);
        var analysis = await context.AiAnalyses.SingleAsync();
        Assert.Equal(
            JobAnalysisStatus.TransientFailure.ToString(),
            analysis.Status);
    }

    [Fact]
    public async Task LowConfidenceAiAnalysisIsPersistedAndFallsBackToRules()
    {
        var analyzer = new StubJobAnalyzer(
            request => new JobAnalysisResult(
                JobAnalysisStatus.Succeeded,
                "test-ai",
                "fixture-model",
                request.SchemaVersion,
                request.RubricVersion,
                new JobAnalysisOutput(40, 0.64, "Insufficient confidence.", []),
                new JobAnalysisUsage(500, 100, 80, 20, null),
                [],
                null));
        await using var host = await PipelineTestHost.CreateAsync(analyzer: analyzer);

        var summary = await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(0, summary.AcceptedAiAnalysisCount);
        Assert.Equal(1, summary.AiFallbackCount);
        Assert.Equal(1, summary.NotificationIntentCount);
        Assert.Equal(1, analyzer.CallCount);

        await using var context = await host.CreateDbContextAsync();
        var analysis = await context.AiAnalyses.SingleAsync();
        Assert.Equal(
            JobAnalysisStatus.InsufficientConfidence.ToString(),
            analysis.Status);
    }

    [Fact]
    public async Task TransientAiFailureRetriesOnlyAfterConfiguredCooldown()
    {
        var analyzer = new StubJobAnalyzer(
            request => JobAnalysisResult.Failure(
                JobAnalysisStatus.TransientFailure,
                "test-ai",
                null,
                request.SchemaVersion,
                request.RubricVersion,
                "FixtureUnavailable"));
        await using var host = await PipelineTestHost.CreateAsync(analyzer: analyzer);

        await host.Orchestrator.RunDueAsync(CancellationToken.None);
        host.Time.Advance(TimeSpan.FromMinutes(30));
        await host.Orchestrator.RunDueAsync(CancellationToken.None);
        host.Time.Advance(TimeSpan.FromMinutes(31));
        await host.Orchestrator.RunDueAsync(CancellationToken.None);

        Assert.Equal(2, analyzer.CallCount);
        await using var context = await host.CreateDbContextAsync();
        Assert.Equal(1, await context.AiAnalyses.CountAsync());
    }

    private sealed class PipelineTestHost : IAsyncDisposable
    {
        private readonly string _dataDirectory;
        private readonly ManualTimeProvider? _manualTime;

        private PipelineTestHost(
            string dataDirectory,
            ServiceProvider services,
            ManualTimeProvider? time,
            StubJobSource source)
        {
            _dataDirectory = dataDirectory;
            _manualTime = time;
            Services = services;
            Source = source;
        }

        public ServiceProvider Services { get; }

        public ManualTimeProvider Time =>
            _manualTime
            ?? throw new InvalidOperationException(
                "This pipeline host uses the system time provider.");

        public StubJobSource Source { get; }

        public StubNotificationChannel NotificationChannel =>
            Services.GetRequiredService<StubNotificationChannel>();

        public ScanOrchestrator Orchestrator =>
            Services.GetRequiredService<ScanOrchestrator>();

        public NotificationOutboxDispatcher Dispatcher =>
            Services.GetRequiredService<NotificationOutboxDispatcher>();

        public static async Task<PipelineTestHost> CreateAsync(
            Func<JobSourceRecord, CancellationToken, Task<JobSourceResult>>? fetch = null,
            IJobAnalyzer? analyzer = null,
            TimeProvider? timeProvider = null,
            TimeSpan? sourceLeaseDuration = null,
            Func<
                JobSourceRequest,
                JobSourceRecord,
                CancellationToken,
                Task<JobSourceResult>>? contextualFetch = null,
            IReadOnlyList<JobSourceSubscriptionDefinition>? subscriptions = null,
            Func<IServiceProvider, IJobIngestionStore>? ingestionStoreFactory = null)
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "JobHunter.IntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var manualTime = timeProvider is null
                ? new ManualTimeProvider(At("2026-09-16T10:00:00Z"))
                : null;
            var time = timeProvider ?? manualTime!;
            var record = CreateRecord(time.GetUtcNow());
            var source = new StubJobSource(
                fetch ?? ((job, _) => Task.FromResult(JobSourceResult.Succeeded([job], null))),
                record,
                contextualFetch);
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:DataDirectory"] = dataDirectory,
                    ["Storage:DatabaseFileName"] = "pipeline.db"
                });
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(time);
            services.AddJobHunterInfrastructure(configuration);
            if (ingestionStoreFactory is not null)
            {
                services.AddSingleton(ingestionStoreFactory);
            }

            services.AddSingleton<ICandidateProfileLoader>(new StubProfileLoader());
            services.AddSingleton<IJobAnalyzer>(
                analyzer ?? new NullJobAnalyzer());
            services.AddSingleton<IJobSource>(source);
            services.AddSingleton<IJobSourceSubscriptionProvider>(
                new StubSubscriptionProvider(subscriptions));
            services.AddSingleton<INotificationDestinationProvider>(
                new StubDestinationProvider());
            services.AddSingleton<StubNotificationChannel>();
            services.AddSingleton<INotificationChannel>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<StubNotificationChannel>());
            services.AddSingleton(
                new ScanOrchestratorOptions
                {
                    MaximumConcurrentSources = 2,
                    MaximumSubscriptionsPerTick = 10,
                    SourceLeaseDuration =
                        sourceLeaseDuration ?? TimeSpan.FromMinutes(5)
                });
            services.AddSingleton<ScanOrchestrator>();
            services.AddSingleton(
                new NotificationOutboxDispatcherOptions
                {
                    MaximumBatchSize = 10,
                    LeaseDuration = TimeSpan.FromMinutes(2)
                });
            services.AddSingleton<NotificationOutboxDispatcher>();
            var provider = services.BuildServiceProvider(validateScopes: true);

            try
            {
                await provider
                    .GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync(CancellationToken.None);
                return new PipelineTestHost(
                    dataDirectory,
                    provider,
                    manualTime,
                    source);
            }
            catch
            {
                await provider.DisposeAsync();
                Directory.Delete(dataDirectory, recursive: true);
                throw;
            }
        }

        public Task<JobHunterDbContext> CreateDbContextAsync() =>
            Services
                .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
                .CreateDbContextAsync();

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private sealed class StubJobSource(
        Func<JobSourceRecord, CancellationToken, Task<JobSourceResult>> fetch,
        JobSourceRecord record,
        Func<
            JobSourceRequest,
            JobSourceRecord,
            CancellationToken,
            Task<JobSourceResult>>? contextualFetch)
        : IJobSource
    {
        private int _fetchCount;

        public SourceName Name => SourceName.Dou;

        public int FetchCount => Volatile.Read(ref _fetchCount);

        public Task<JobSourceResult> FetchAsync(
            JobSourceRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fetchCount);
            var currentRecord = record with
            {
                RetrievedAtUtc = request.Cursor is null
                ? record.RetrievedAtUtc
                : record.RetrievedAtUtc.AddMinutes(13)
            };
            return contextualFetch is null
                ? fetch(currentRecord, cancellationToken)
                : contextualFetch(request, currentRecord, cancellationToken);
        }
    }

    private sealed class StubSubscriptionProvider(
        IReadOnlyList<JobSourceSubscriptionDefinition>? subscriptions)
        : IJobSourceSubscriptionProvider
    {
        public IReadOnlyList<JobSourceSubscriptionDefinition> GetSubscriptions() =>
            subscriptions
            ??
            [
                new(
                SourceName.Dou,
                "test",
                new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                ".NET",
                10,
                TimeSpan.FromMinutes(12),
                true)
            ];
    }

    private sealed class DelayedIngestionStore(
        IJobIngestionStore inner,
        string delayedQueryId,
        TaskCompletionSource persistenceStarted,
        TimeSpan delay)
        : IJobIngestionStore
    {
        public async Task<JobIngestionResult> PersistAsync(
            Guid sourceRunId,
            Guid sourceSubscriptionId,
            string? queryId,
            IReadOnlyCollection<JobSourceRecord> records,
            CancellationToken cancellationToken)
        {
            if (queryId == delayedQueryId)
            {
                persistenceStarted.TrySetResult();
                await Task.Delay(delay, cancellationToken);
            }

            return await inner.PersistAsync(
                sourceRunId,
                sourceSubscriptionId,
                queryId,
                records,
                cancellationToken);
        }
    }

    private sealed class StubDestinationProvider : INotificationDestinationProvider
    {
        public IReadOnlyList<NotificationDestination> GetDestinations() =>
            [new("telegram-test")];
    }

    private sealed class StubNotificationChannel : INotificationChannel
    {
        private int _sendCount;

        public string DestinationId => "telegram-test";

        public TimeSpan MinimumSendInterval => TimeSpan.Zero;

        public int SendCount => Volatile.Read(ref _sendCount);

        public Task<NotificationSendResult> SendAsync(
            JobNotification notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _sendCount);
            return Task.FromResult(NotificationSendResult.Sent("test-message-1"));
        }
    }

    private sealed class StubJobAnalyzer(
        Func<JobAnalysisRequest, JobAnalysisResult> analyze)
        : IJobAnalyzer
    {
        private int _callCount;

        public JobAnalyzerCapabilities Capabilities { get; } =
            new("test-ai", true, true, 48_000, 4_000);

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new JobAnalyzerAvailability(true, "Ready", "fixture-model"));
        }

        public Task<JobAnalysisResult> AnalyzeAsync(
            JobAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(analyze(request));
        }
    }

    private sealed class StubProfileLoader : ICandidateProfileLoader
    {
        public Task<LoadedCandidateProfile> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = new CandidateProfile
            {
                TargetTitles = ["Backend Engineer"],
                Skills =
                [
                    new CandidateSkill
                    {
                        Name = ".NET",
                        Category = SkillCategory.Core,
                        Required = true
                    }
                ]
            };
            return Task.FromResult(
                new LoadedCandidateProfile(
                    profile,
                    "{\"schemaVersion\":1}",
                    "sha256:test-profile",
                    "C:\\profiles\\test.yaml",
                    null));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public void Advance(TimeSpan duration)
        {
            lock (_sync)
            {
                _utcNow = _utcNow.Add(duration);
            }
        }
    }

    private static JobSourceRecord CreateRecord(DateTimeOffset retrievedAtUtc) =>
        new()
        {
            Source = SourceName.Dou,
            SourceJobId = NativeSourceId.Create("pipeline-1"),
            SourceUrl = new Uri("https://jobs.dou.ua/vacancies/123456/"),
            CanonicalUrl = CanonicalJobUrl.Create(
                "https://jobs.dou.ua/vacancies/123456/?utm_source=test"),
            SourceGuid = "pipeline-1",
            Title = "Senior .NET Backend Engineer",
            Company = "Example",
            DescriptionHtml = "<p>.NET C# backend role</p>",
            DescriptionText = ".NET C# backend role",
            Locations = ["Remote"],
            WorkplaceMode = WorkplaceMode.Remote,
            EmploymentType = EmploymentType.FullTime,
            Seniority = "Senior",
            Skills = [".NET", "C#"],
            Categories = [".NET"],
            CompensationPeriod = CompensationPeriod.Unknown,
            PublishedAtUtc = retrievedAtUtc.AddHours(-1),
            PublishedAtPrecision = PublishedAtPrecision.DateTime,
            ParserVersion = "fixture-v1",
            ContentHash = "sha256:content",
            RawPayloadHash = "sha256:payload",
            RetrievedAtUtc = retrievedAtUtc
        };

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
