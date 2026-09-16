using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.AI.Copilot;

public sealed class CopilotJobAnalyzer : IJobAnalyzer, IDisposable
{
    private readonly ICopilotSessionRunner _sessionRunner;
    private readonly CopilotOptions _options;
    private readonly SemaphoreSlim _executionGate;
    private readonly SemaphoreSlim _queueSlots;

    internal CopilotJobAnalyzer(
        ICopilotSessionRunner sessionRunner,
        IOptions<CopilotOptions> options)
    {
        _sessionRunner = sessionRunner;
        _options = options.Value;
        _executionGate = new SemaphoreSlim(
            _options.MaximumConcurrency,
            _options.MaximumConcurrency);
        _queueSlots = new SemaphoreSlim(
            _options.QueueCapacity,
            _options.QueueCapacity);
        Capabilities = new JobAnalyzerCapabilities(
            CopilotOptions.ProviderName,
            true,
            true,
            _options.MaximumInputCharacters,
            _options.MaximumOutputCharacters);
    }

    public JobAnalyzerCapabilities Capabilities { get; }

    public void Dispose()
    {
        _executionGate.Dispose();
        _queueSlots.Dispose();
    }

    public async Task<JobAnalyzerAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await _sessionRunner.CheckAvailabilityAsync(
                cancellationToken);
            return new JobAnalyzerAvailability(
                availability.IsAvailable,
                availability.StatusCode,
                availability.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return new JobAnalyzerAvailability(
                false,
                "CopilotTransportFailure",
                null);
        }
        catch (InvalidOperationException)
        {
            return new JobAnalyzerAvailability(
                false,
                "CopilotRuntimeFailure",
                null);
        }
    }

    public async Task<JobAnalysisResult> AnalyzeAsync(
        JobAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = CopilotAnalysisPromptBuilder.Build(request);
        if (prompt.Length > _options.MaximumInputCharacters)
        {
            return Failure(
                request,
                JobAnalysisStatus.OverBudget,
                "InputTooLarge",
                prompt.Length);
        }

        if (!_queueSlots.Wait(0, cancellationToken))
        {
            return Failure(
                request,
                JobAnalysisStatus.OverBudget,
                "AiQueueFull",
                prompt.Length);
        }

        using var deadlineCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = request.DeadlineUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _queueSlots.Release();
            return Failure(
                request,
                JobAnalysisStatus.TimedOut,
                "AnalysisDeadlineExceeded",
                prompt.Length);
        }

        deadlineCancellation.CancelAfter(remaining);
        var enteredExecution = false;
        try
        {
            await _executionGate.WaitAsync(deadlineCancellation.Token);
            enteredExecution = true;
            _queueSlots.Release();

            for (var attempt = 0; ; attempt++)
            {
                CopilotRunOutcome outcome;
                try
                {
                    outcome = await _sessionRunner.RunAsync(
                        request,
                        prompt,
                        deadlineCancellation.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    return Failure(
                        request,
                        JobAnalysisStatus.TimedOut,
                        "AnalysisDeadlineExceeded",
                        prompt.Length);
                }
                catch (IOException)
                {
                    outcome = CopilotRunOutcome.Failure(
                        JobAnalysisStatus.TransientFailure,
                        "CopilotTransportFailure");
                }
                catch (InvalidOperationException)
                {
                    outcome = CopilotRunOutcome.Failure(
                        JobAnalysisStatus.PermanentFailure,
                        "CopilotRuntimeFailure");
                }

                if (outcome.Status == JobAnalysisStatus.TransientFailure
                    && attempt < _options.MaximumTransientRetries)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            _options.TransientRetryDelaySeconds * (attempt + 1)),
                        deadlineCancellation.Token);
                    continue;
                }

                return ToResult(request, prompt.Length, outcome);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request,
                JobAnalysisStatus.TimedOut,
                "AnalysisDeadlineExceeded",
                prompt.Length);
        }
        finally
        {
            if (enteredExecution)
            {
                _executionGate.Release();
            }
            else
            {
                _queueSlots.Release();
            }
        }
    }

    private JobAnalysisResult ToResult(
        JobAnalysisRequest request,
        int inputCharacters,
        CopilotRunOutcome outcome)
    {
        var usage = new JobAnalysisUsage(
            inputCharacters,
            outcome.OutputCharacters,
            outcome.InputTokens,
            outcome.OutputTokens,
            null);
        if (outcome.Status == JobAnalysisStatus.Succeeded
            && outcome.Output is not null)
        {
            return new JobAnalysisResult(
                JobAnalysisStatus.Succeeded,
                Capabilities.Provider,
                outcome.Model,
                request.SchemaVersion,
                request.RubricVersion,
                outcome.Output,
                usage,
                request.Warnings,
                null);
        }

        return JobAnalysisResult.Failure(
            outcome.Status,
            Capabilities.Provider,
            outcome.Model,
            request.SchemaVersion,
            request.RubricVersion,
            outcome.FailureCode ?? "CopilotAnalysisFailed",
            usage,
            request.Warnings);
    }

    private JobAnalysisResult Failure(
        JobAnalysisRequest request,
        JobAnalysisStatus status,
        string failureCode,
        int inputCharacters) =>
        JobAnalysisResult.Failure(
            status,
            Capabilities.Provider,
            null,
            request.SchemaVersion,
            request.RubricVersion,
            failureCode,
            new JobAnalysisUsage(
                inputCharacters,
                0,
                null,
                null,
                null),
            request.Warnings);
}
