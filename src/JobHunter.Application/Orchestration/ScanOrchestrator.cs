using JobHunter.AI.Abstractions;
using JobHunter.Application.Evaluation;
using JobHunter.Application.Notifications;
using JobHunter.Application.Persistence;
using JobHunter.Application.Profiles;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;

namespace JobHunter.Application.Orchestration;

public sealed class ScanOrchestrator : IDisposable
{
    public const int NotificationVersion = 2;

    private readonly Dictionary<SourceName, IJobSource> _sources;
    private readonly IReadOnlyList<IJobSourceSubscriptionProvider> _subscriptionProviders;
    private readonly ISourceSubscriptionStore _subscriptionStore;
    private readonly ISourceRunStore _sourceRunStore;
    private readonly IJobIngestionStore _ingestionStore;
    private readonly ICandidateProfileLoader _profileLoader;
    private readonly ICandidateProfileStore _profileStore;
    private readonly IRuleEvaluationStore _evaluationStore;
    private readonly IJobAnalyzer _jobAnalyzer;
    private readonly IAiAnalysisStore _aiAnalysisStore;
    private readonly INotificationOutboxStore _outboxStore;
    private readonly IReadOnlyList<NotificationDestination> _destinations;
    private readonly TimeProvider _timeProvider;
    private readonly ScanOrchestratorOptions _options;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);

    public ScanOrchestrator(
        IEnumerable<IJobSource> sources,
        IEnumerable<IJobSourceSubscriptionProvider> subscriptionProviders,
        ISourceSubscriptionStore subscriptionStore,
        ISourceRunStore sourceRunStore,
        IJobIngestionStore ingestionStore,
        ICandidateProfileLoader profileLoader,
        ICandidateProfileStore profileStore,
        IRuleEvaluationStore evaluationStore,
        IJobAnalyzer jobAnalyzer,
        IAiAnalysisStore aiAnalysisStore,
        INotificationOutboxStore outboxStore,
        IEnumerable<INotificationDestinationProvider> destinationProviders,
        TimeProvider timeProvider,
        ScanOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(subscriptionProviders);
        ArgumentNullException.ThrowIfNull(destinationProviders);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _sources = sources.ToDictionary(source => source.Name);
        _subscriptionProviders = [.. subscriptionProviders];
        _subscriptionStore = subscriptionStore;
        _sourceRunStore = sourceRunStore;
        _ingestionStore = ingestionStore;
        _profileLoader = profileLoader;
        _profileStore = profileStore;
        _evaluationStore = evaluationStore;
        _jobAnalyzer = jobAnalyzer;
        _aiAnalysisStore = aiAnalysisStore;
        _outboxStore = outboxStore;
        _destinations = destinationProviders
            .SelectMany(provider => provider.GetDestinations())
            .DistinctBy(destination => destination.Id, StringComparer.Ordinal)
            .ToArray();
        _timeProvider = timeProvider;
        _options = options;
    }

    public void Dispose() => _persistenceGate.Dispose();

    public Task<ScanBatchSummary> RunDueAsync(CancellationToken cancellationToken) => RunAsync(null, false, cancellationToken);
    public Task<ScanBatchSummary> RunOnceAsync(SourceName source, CancellationToken cancellationToken) => RunAsync(source, true, cancellationToken);

    private async Task<ScanBatchSummary> RunAsync(SourceName? source, bool ignoreSchedule, CancellationToken cancellationToken)
    {
        var definitions = _subscriptionProviders
            .SelectMany(provider => provider.GetSubscriptions())
            .ToArray();
        var now = _timeProvider.GetUtcNow();
        await _subscriptionStore.SynchronizeAsync(definitions, now, cancellationToken);
        await _sourceRunStore.RecoverExpiredAsync(now, cancellationToken);

        var dueSubscriptions = ignoreSchedule
            ? await _subscriptionStore.GetRunnableAsync(source ?? throw new InvalidOperationException("A source is required for an immediate scan."), _options.MaximumSubscriptionsPerTick, cancellationToken)
            : await _subscriptionStore.GetDueAsync(now, _options.MaximumSubscriptionsPerTick, cancellationToken);

        if (dueSubscriptions.Count == 0)
        {
            return ScanBatchSummary.Empty;
        }

        var loadedProfile = await _profileLoader.LoadAsync(cancellationToken);
        var profileSnapshot = await _profileStore.SaveAsync(
            loadedProfile,
            now,
            cancellationToken);
        var results = new System.Collections.Concurrent.ConcurrentBag<SourceScanSummary>();

        await Parallel.ForEachAsync(
            dueSubscriptions,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _options.MaximumConcurrentSources
            },
            async (subscription, token) =>
            {
                var result = await ProcessSubscriptionAsync(
                    subscription,
                    loadedProfile,
                    profileSnapshot,
                    ignoreSchedule,
                    token);
                results.Add(result);
            });

        return ScanBatchSummary.From(results);
    }

    private async Task<SourceScanSummary> ProcessSubscriptionAsync(
        DueJobSourceSubscription subscription,
        LoadedCandidateProfile loadedProfile,
        CandidateProfileSnapshotReference profileSnapshot,
        bool ignoreSchedule,
        CancellationToken cancellationToken)
    {
        var lease = ignoreSchedule
            ? await _sourceRunStore.TryStartImmediatelyAsync(
                subscription.Id,
                _timeProvider.GetUtcNow(),
                _options.SourceLeaseDuration,
                cancellationToken)
            : await _sourceRunStore.TryStartAsync(
                subscription.Id,
                _timeProvider.GetUtcNow(),
                _options.SourceLeaseDuration,
                cancellationToken);
        if (lease is null)
        {
            return SourceScanSummary.Skipped(subscription.Source);
        }

        using var heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = MaintainLeaseAsync(
            lease,
            heartbeatCancellation);
        var runCancellationToken = heartbeatCancellation.Token;
        var leaseCompleted = false;

        try
        {
            var sourceResult = await FetchAsync(subscription, runCancellationToken);
            var ingestionResult = new JobIngestionResult(0, 0, 0, 0);
            var evaluations = 0;
            var qualifying = 0;
            var intents = 0;
            var suppressedIntents = 0;
            var aiAnalyses = 0;
            var acceptedAiAnalyses = 0;
            var deferredAiAnalyses = 0;

            if (sourceResult.Status is SourceRunStatus.Succeeded or SourceRunStatus.Partial)
            {
                try
                {
                    var evaluatedJobs =
                        new List<(PersistedJob Job, DeterministicEvaluation Evaluation)>();

                    await _persistenceGate.WaitAsync(runCancellationToken);
                    try
                    {
                        ingestionResult = await _ingestionStore.PersistAsync(
                            lease.SourceRunId,
                            subscription.Id,
                            subscription.QueryId,
                            sourceResult.Jobs,
                            runCancellationToken);

                        foreach (var job in ingestionResult.Jobs)
                        {
                            var evaluation = DeterministicJobEvaluator.Evaluate(
                                loadedProfile.Profile,
                                job.Record);
                            await _evaluationStore.SaveAsync(
                                job.JobId,
                                profileSnapshot.Id,
                                job.RevisionNumber,
                                evaluation,
                                _timeProvider.GetUtcNow(),
                                runCancellationToken);
                            evaluations++;

                            if (evaluation.PassedHardFilters)
                            {
                                evaluatedJobs.Add((job, evaluation));
                            }
                        }
                    }
                    finally
                    {
                        _persistenceGate.Release();
                    }

                    foreach (var evaluatedJob in evaluatedJobs)
                    {
                        aiAnalyses++;
                        var analysis = await GetOrAnalyzeAsync(
                            loadedProfile,
                            profileSnapshot,
                            evaluatedJob.Job,
                            runCancellationToken);
                        if (analysis.IsSuccessful)
                        {
                            acceptedAiAnalyses++;
                        }
                        else
                        {
                            deferredAiAnalyses++;
                        }

                        var decision = JobQualificationPolicy.Decide(
                            evaluatedJob.Evaluation,
                            analysis,
                            _options.MinimumAiConfidence,
                            _options.MinimumAiFitScore);
                        if (!decision.Qualifies)
                        {
                            continue;
                        }

                        qualifying++;
                        await _persistenceGate.WaitAsync(runCancellationToken);
                        try
                        {
                            foreach (var destination in _destinations)
                            {
                                var enqueueOutcome = await _outboxStore.EnqueueAsync(
                                    CreateIntent(
                                        destination,
                                        evaluatedJob.Job,
                                        decision),
                                    _timeProvider.GetUtcNow(),
                                        runCancellationToken);
                                if (enqueueOutcome == NotificationEnqueueOutcome.Created)
                                {
                                    intents++;
                                }
                                else if (enqueueOutcome is
                                    NotificationEnqueueOutcome.DestinationDisabled
                                    or NotificationEnqueueOutcome.QueueFull
                                    or NotificationEnqueueOutcome.PossibleDuplicateAlreadySent)
                                {
                                    suppressedIntents++;
                                }
                            }
                        }
                        finally
                        {
                            _persistenceGate.Release();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    sourceResult = JobSourceResult.Failed(
                        "PipelineFailure",
                        $"The persisted scoring pipeline failed with {exception.GetType().Name}.",
                        _timeProvider.GetUtcNow().Add(subscription.Interval));
                }
            }

            await _persistenceGate.WaitAsync(runCancellationToken);
            try
            {
                heartbeatCancellation.Cancel();
                var heartbeatFailure = await heartbeatTask;
                if (heartbeatFailure is not null)
                {
                    throw new InvalidOperationException(
                        "The source run heartbeat failed before completion.",
                        heartbeatFailure);
                }

                await _sourceRunStore.CompleteAsync(
                    lease.SourceRunId,
                    lease.LeaseToken,
                    sourceResult,
                    ingestionResult,
                    _timeProvider.GetUtcNow(),
                    subscription.Interval,
                    cancellationToken);
                leaseCompleted = true;
            }
            finally
            {
                _persistenceGate.Release();
            }

            return new SourceScanSummary(
                subscription.Source,
                false,
                sourceResult.Status,
                ingestionResult.ObservedCount,
                evaluations,
                qualifying,
                intents,
                suppressedIntents,
                aiAnalyses,
                acceptedAiAnalyses,
                deferredAiAnalyses);
        }
        catch (Exception exception)
        {
            heartbeatCancellation.Cancel();
            var heartbeatFailure = await heartbeatTask;
            Exception? abandonmentFailure = null;
            if (!leaseCompleted)
            {
                try
                {
                    await _sourceRunStore.TryAbandonAsync(
                        lease.SourceRunId,
                        lease.LeaseToken,
                        _timeProvider.GetUtcNow(),
                        cancellationToken.IsCancellationRequested
                            ? "RunCancelled"
                            : "UnhandledPipelineFailure",
                        cancellationToken.IsCancellationRequested
                            ? "The source run was cancelled before completion."
                            : $"The source run failed with {exception.GetType().Name}.",
                        CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    abandonmentFailure = cleanupException;
                }
            }

            var cleanupFailures = new List<Exception>();
            if (heartbeatFailure is not null
                && !ReferenceEquals(exception.InnerException, heartbeatFailure))
            {
                cleanupFailures.Add(heartbeatFailure);
            }

            if (abandonmentFailure is not null)
            {
                cleanupFailures.Add(abandonmentFailure);
            }

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, exception);
                throw new AggregateException(
                    "The source run failed and lease cleanup was not fully successful.",
                    cleanupFailures);
            }

            throw;
        }
    }

    private async Task<Exception?> MaintainLeaseAsync(
        SourceRunLease lease,
        CancellationTokenSource runCancellation)
    {
        var cancellationToken = runCancellation.Token;
        var interval = TimeSpan.FromTicks(
            Math.Max(
                TimeSpan.FromSeconds(1).Ticks,
                _options.SourceLeaseDuration.Ticks / 3));
        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _sourceRunStore.HeartbeatAsync(
                    lease.SourceRunId,
                    lease.LeaseToken,
                    _timeProvider.GetUtcNow(),
                    _options.SourceLeaseDuration,
                    cancellationToken);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            try
            {
                await runCancellation.CancelAsync();
            }
            catch (Exception cancellationException)
            {
                return new AggregateException(exception, cancellationException);
            }

            return exception;
        }
    }

    private async Task<JobAnalysisResult> GetOrAnalyzeAsync(
        LoadedCandidateProfile loadedProfile,
        CandidateProfileSnapshotReference profileSnapshot,
        PersistedJob job,
        CancellationToken cancellationToken)
    {
        var request = JobAnalysisRequestFactory.Create(
            loadedProfile,
            profileSnapshot,
            job,
            _jobAnalyzer.Capabilities,
            _timeProvider.GetUtcNow(),
            _options.AiAnalysisTimeout);

        await _persistenceGate.WaitAsync(cancellationToken);
        StoredJobAnalysis? existing;
        try
        {
            existing = await _aiAnalysisStore.GetAsync(
                job.JobId,
                profileSnapshot.Id,
                job.RevisionNumber,
                _jobAnalyzer.Capabilities.Provider,
                request.SchemaVersion,
                request.RubricVersion,
                cancellationToken);
        }
        finally
        {
            _persistenceGate.Release();
        }

        if (existing is not null)
        {
            var retryAtUtc = existing.RecordedAtUtc.Add(
                _options.AiTransientFailureRetryDelay);
            if (!IsRetryable(existing.Result.Status)
                || retryAtUtc > _timeProvider.GetUtcNow())
            {
                return existing.Result;
            }
        }

        JobAnalysisResult result;
        try
        {
            result = await _jobAnalyzer.AnalyzeAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = JobAnalysisResult.Failure(
                JobAnalysisStatus.PermanentFailure,
                _jobAnalyzer.Capabilities.Provider,
                null,
                request.SchemaVersion,
                request.RubricVersion,
                "UnhandledAnalyzerFailure",
                warnings: [$"AnalyzerException:{exception.GetType().Name}"]);
        }

        result = NormalizeAnalysisResult(request, result);
        if (result.IsSuccessful
            && result.Output!.Confidence < _options.MinimumAiConfidence)
        {
            result = result.MarkInsufficientConfidence();
        }

        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            await _aiAnalysisStore.SaveAsync(
                job.JobId,
                profileSnapshot.Id,
                job.RevisionNumber,
                result,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        finally
        {
            _persistenceGate.Release();
        }

        return result;
    }

    private static bool IsRetryable(JobAnalysisStatus status) =>
        status is JobAnalysisStatus.InsufficientConfidence
            or JobAnalysisStatus.InvalidOutput
            or JobAnalysisStatus.TimedOut
            or JobAnalysisStatus.Unavailable
            or JobAnalysisStatus.OverBudget
            or JobAnalysisStatus.TransientFailure;

    private JobAnalysisResult NormalizeAnalysisResult(
        JobAnalysisRequest request,
        JobAnalysisResult result)
    {
        if (string.Equals(
                result.Provider,
                _jobAnalyzer.Capabilities.Provider,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                result.SchemaVersion,
                request.SchemaVersion,
                StringComparison.Ordinal)
            && string.Equals(
                result.RubricVersion,
                request.RubricVersion,
                StringComparison.Ordinal)
            && (result.Status != JobAnalysisStatus.Succeeded
                || result.Output is not null))
        {
            return result;
        }

        return JobAnalysisResult.Failure(
            JobAnalysisStatus.InvalidOutput,
            _jobAnalyzer.Capabilities.Provider,
            result.Model,
            request.SchemaVersion,
            request.RubricVersion,
            "AnalyzerContractViolation",
            result.Usage,
            result.Warnings);
    }

    private async Task<JobSourceResult> FetchAsync(
        DueJobSourceSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (!_sources.TryGetValue(subscription.Source, out var source))
        {
            return JobSourceResult.Failed(
                "AdapterUnavailable",
                $"No enabled adapter is registered for source '{subscription.Source}'.",
                _timeProvider.GetUtcNow().Add(subscription.Interval));
        }

        try
        {
            return await source.FetchAsync(
                new JobSourceRequest(
                    subscription.Id,
                    subscription.Endpoint,
                    subscription.QueryId,
                    subscription.Cursor,
                    subscription.MaximumItems),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return JobSourceResult.Failed(
                "UnhandledSourceFailure",
                $"The source adapter failed with {exception.GetType().Name}.",
                _timeProvider.GetUtcNow().Add(subscription.Interval));
        }
    }

    private static NotificationIntent CreateIntent(
        NotificationDestination destination,
        PersistedJob job,
        JobQualificationDecision decision) =>
        new(
            destination.Id,
            job.JobId,
            NotificationVersion,
            new JobNotification(
                job.Record.Title,
                job.Record.Company,
                job.Record.Locations,
                job.Record.WorkplaceMode,
                decision.Score,
                job.Record.CompensationMinimum,
                job.Record.CompensationMaximum,
                job.Record.CompensationCurrency,
                job.Record.CompensationPeriod,
                job.Record.PublishedAtUtc,
                job.Record.CanonicalUrl.ToString())
            {
                AiSummary = decision.AiSummary,
                Strengths = decision.Strengths,
                Concerns = decision.Concerns,
                PassedHardFilters = decision.PassedHardFilters,
                AiInputTokens = decision.AiUsage?.InputTokens,
                AiOutputTokens = decision.AiUsage?.OutputTokens,
                AiCredits = decision.AiUsage?.AiCredits,
                AiRequestCount = decision.AiUsage?.RequestCount ?? 0
            })
        {
            SuppressPossibleDuplicateNotifications =
                destination.SuppressPossibleDuplicateNotifications
        };
}

public sealed class ScanOrchestratorOptions
{
    public int MaximumConcurrentSources { get; init; } = 2;

    public int MaximumSubscriptionsPerTick { get; init; } = 16;

    public TimeSpan SourceLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan AiAnalysisTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public double MinimumAiConfidence { get; init; } = 0.65;

    public int MinimumAiFitScore { get; init; } = 75;

    public TimeSpan AiTransientFailureRetryDelay { get; init; } =
        TimeSpan.FromHours(1);

    internal void Validate()
    {
        if (MaximumConcurrentSources is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConcurrentSources),
                MaximumConcurrentSources,
                "Source concurrency must be between 1 and 16.");
        }

        if (MaximumSubscriptionsPerTick is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSubscriptionsPerTick),
                MaximumSubscriptionsPerTick,
                "The maximum subscriptions per tick must be between 1 and 1000.");
        }

        if (SourceLeaseDuration < TimeSpan.FromSeconds(5)
            || SourceLeaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(SourceLeaseDuration),
                SourceLeaseDuration,
                "A source lease must be between 5 seconds and 1 hour.");
        }

        if (AiAnalysisTimeout < TimeSpan.FromSeconds(10)
            || AiAnalysisTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AiAnalysisTimeout),
                AiAnalysisTimeout,
                "An AI analysis timeout must be between 10 seconds and 5 minutes.");
        }

        if (MinimumAiConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumAiConfidence),
                MinimumAiConfidence,
                "Minimum AI confidence must be between 0 and 1.");
        }

        if (MinimumAiFitScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumAiFitScore),
                MinimumAiFitScore,
                "Minimum AI fit score must be between 0 and 100.");
        }

        if (AiTransientFailureRetryDelay < TimeSpan.FromMinutes(1)
            || AiTransientFailureRetryDelay > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AiTransientFailureRetryDelay),
                AiTransientFailureRetryDelay,
                "The AI transient failure retry delay must be between 1 minute and 1 day.");
        }
    }
}

public sealed record SourceScanSummary(
    SourceName Source,
    bool WasSkipped,
    SourceRunStatus? Status,
    int ObservedCount,
    int EvaluationCount,
    int QualifyingCount,
    int NotificationIntentCount,
    int SuppressedNotificationIntentCount,
    int AiAnalysisCount,
    int AcceptedAiAnalysisCount,
    int DeferredAiAnalysisCount)
{
    public static SourceScanSummary Skipped(SourceName source) =>
        new(source, true, null, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record ScanBatchSummary(
    int DueCount,
    int StartedCount,
    int SucceededCount,
    int PartialCount,
    int BlockedCount,
    int FailedCount,
    int ObservedCount,
    int EvaluationCount,
    int QualifyingCount,
    int NotificationIntentCount,
    int SuppressedNotificationIntentCount,
    int AiAnalysisCount,
    int AcceptedAiAnalysisCount,
    int DeferredAiAnalysisCount)
{
    public static ScanBatchSummary Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ScanBatchSummary From(
        IReadOnlyCollection<SourceScanSummary> results) =>
        new(
            results.Count,
            results.Count(result => !result.WasSkipped),
            results.Count(result => result.Status == SourceRunStatus.Succeeded),
            results.Count(result => result.Status == SourceRunStatus.Partial),
            results.Count(result => result.Status == SourceRunStatus.Blocked),
            results.Count(result => result.Status == SourceRunStatus.Failed),
            results.Sum(result => result.ObservedCount),
            results.Sum(result => result.EvaluationCount),
            results.Sum(result => result.QualifyingCount),
            results.Sum(result => result.NotificationIntentCount),
            results.Sum(result => result.SuppressedNotificationIntentCount),
            results.Sum(result => result.AiAnalysisCount),
            results.Sum(result => result.AcceptedAiAnalysisCount),
            results.Sum(result => result.DeferredAiAnalysisCount));
}
