using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GitHub.Copilot;
using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using JobHunter.Application.Security;
using JobHunter.Application.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace JobHunter.AI.Copilot;

internal interface ICopilotSessionRunner
{
    Task<CopilotAvailabilityOutcome> CheckAvailabilityAsync(
        CancellationToken cancellationToken);

    Task<CopilotRunOutcome> RunAsync(
        JobAnalysisRequest request,
        string prompt,
        CancellationToken cancellationToken);
}

internal sealed class CopilotSessionRunner(
    IAppDataDirectory appDataDirectory,
    ISecretReader secretReader,
    IOptions<CopilotOptions> options)
    : ICopilotSessionRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public async Task<CopilotAvailabilityOutcome> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var configuredOptions = options.Value;
        var clientOptions = CreateClientOptions(out _);

        await using var client = new CopilotClient(clientOptions);
        await client.StartAsync(cancellationToken);
        var auth = await client.GetAuthStatusAsync(cancellationToken);
        if (!auth.IsAuthenticated)
        {
            return new CopilotAvailabilityOutcome(
                false,
                "CopilotAuthenticationUnavailable",
                null);
        }

        var models = await client.ListModelsAsync(cancellationToken);
        var configuredModel = string.IsNullOrWhiteSpace(configuredOptions.Model)
            ? "auto"
            : configuredOptions.Model.Trim();
        if (!string.Equals(configuredModel, "auto", StringComparison.OrdinalIgnoreCase)
            && !models.Any(
                model => string.Equals(
                    model.Id,
                    configuredModel,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return new CopilotAvailabilityOutcome(
                false,
                "CopilotModelUnavailable",
                configuredModel);
        }

        return new CopilotAvailabilityOutcome(
            models.Count > 0,
            models.Count > 0 ? "Ready" : "CopilotModelsUnavailable",
            configuredModel);
    }

    public async Task<CopilotRunOutcome> RunAsync(
        JobAnalysisRequest request,
        string prompt,
        CancellationToken cancellationToken)
    {
        var configuredOptions = options.Value;
        var clientOptions = CreateClientOptions(out var workingDirectory);

        await using var client = new CopilotClient(clientOptions);
        await client.StartAsync(cancellationToken);
        var auth = await client.GetAuthStatusAsync(cancellationToken);
        if (!auth.IsAuthenticated)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.Unavailable,
                "CopilotAuthenticationUnavailable");
        }

        var submission = new SubmissionCapture(
            request,
            configuredOptions.MaximumOutputCharacters);
        var tool = CreateSubmissionTool(submission);
        var session = await client.CreateSessionAsync(
            CopilotSessionConfigurationFactory.Create(
                configuredOptions,
                tool,
                workingDirectory),
            cancellationToken);
        var sessionId = session.SessionId;
        var usage = new UsageCapture();
        var error = new ErrorCapture();
        var outcome = CopilotRunOutcome.Failure(
            JobAnalysisStatus.PermanentFailure,
            "CopilotSessionDidNotComplete");
        OperationCanceledException? cancellationException = null;
        var abortRequired = false;
        var cleanupSucceeded = true;

        try
        {
            using var usageSubscription = session.On<AssistantUsageEvent>(usage.Capture);
            using var errorSubscription = session.On<SessionErrorEvent>(error.Capture);
            var remaining = request.DeadlineUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                outcome = CopilotRunOutcome.Failure(
                    JobAnalysisStatus.TimedOut,
                    "AnalysisDeadlineExceeded");
            }
            else
            {
                var assistant = await session.SendAndWaitAsync(
                    prompt,
                    remaining,
                    cancellationToken);
                outcome = CreateOutcome(
                    submission,
                    usage,
                    error,
                    assistant?.Data.Model);
            }
        }
        catch (TimeoutException)
        {
            abortRequired = true;
            outcome = CopilotRunOutcome.Failure(
                JobAnalysisStatus.TimedOut,
                "CopilotTimeout");
        }
        catch (OperationCanceledException exception)
        {
            abortRequired = true;
            cancellationException = exception;
        }
        finally
        {
            if (abortRequired)
            {
                cleanupSucceeded = await AbortSessionAsync(session);
            }

            cleanupSucceeded = await DisposeSessionAsync(session)
                && cleanupSucceeded;
        }

        cleanupSucceeded = await DeleteSessionAsync(client, sessionId)
            && cleanupSucceeded;
        if (cancellationException is not null)
        {
            ExceptionDispatchInfo.Capture(cancellationException).Throw();
        }

        if (!cleanupSucceeded)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.PermanentFailure,
                "CopilotSessionCleanupFailed");
        }

        return outcome;
    }

    private CopilotClientOptions CreateClientOptions(
        out string workingDirectory)
    {
        var baseDirectory = appDataDirectory.GetPath("copilot");
        workingDirectory = appDataDirectory.GetPath("copilot-workspace");
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(workingDirectory);

        var token = secretReader.GetSecret(
            CopilotOptions.GitHubTokenConfigurationKey);
        return new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = baseDirectory,
            WorkingDirectory = workingDirectory,
            LogLevel = CopilotLogLevel.None,
            GitHubToken = token,
            UseLoggedInUser = token is null
        };
    }

    private static async Task<bool> AbortSessionAsync(CopilotSession session)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await session.AbortAsync(cleanupCancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> DisposeSessionAsync(CopilotSession session)
    {
        try
        {
            await session.DisposeAsync();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static AIFunction CreateSubmissionTool(SubmissionCapture capture) =>
        CopilotTool.DefineTool(
            (Func<double, string, List<JobAnalysisCriterionSubmission>, Task<object>>)
                capture.SubmitAsync,
            new CopilotToolOptions
            {
                SkipPermission = true,
                IsTerminal = true,
                Defer = CopilotToolDefer.Never
            },
            new AIFunctionFactoryOptions
            {
                Name = CopilotSessionConfigurationFactory.SubmissionToolName,
                Description =
                    "Submit the complete structured job-fit analysis exactly once."
            });

    private static CopilotRunOutcome CreateOutcome(
        SubmissionCapture submission,
        UsageCapture usage,
        ErrorCapture error,
        string? assistantModel)
    {
        if (submission.Validation?.IsValid == true)
        {
            return new CopilotRunOutcome(
                JobAnalysisStatus.Succeeded,
                submission.Validation.Output,
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens,
                submission.OutputCharacters,
                null);
        }

        if (submission.Validation is not null)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.InvalidOutput,
                submission.Validation.FailureCode ?? "InvalidStructuredOutput",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens,
                submission.OutputCharacters);
        }

        if (error.StatusCode == 429)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.TransientFailure,
                "CopilotRateLimited",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens);
        }

        if (error.StatusCode is 401 or 403)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.Refused,
                "CopilotAccessDenied",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens);
        }

        if (error.HasError)
        {
            return CopilotRunOutcome.Failure(
                error.StatusCode is >= 500
                    ? JobAnalysisStatus.TransientFailure
                    : JobAnalysisStatus.PermanentFailure,
                error.ErrorCode ?? "CopilotSessionError",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens);
        }

        return CopilotRunOutcome.Failure(
            JobAnalysisStatus.InvalidOutput,
            "MissingStructuredOutput",
            usage.Model ?? assistantModel,
            usage.InputTokens,
            usage.OutputTokens);
    }

    private static async Task<bool> DeleteSessionAsync(
        CopilotClient client,
        string sessionId)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await client.DeleteSessionAsync(
                sessionId,
                cleanupCancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class SubmissionCapture(
        JobAnalysisRequest request,
        int maximumOutputCharacters)
    {
        private readonly object _sync = new();

        public JobAnalysisValidationResult? Validation { get; private set; }

        public int OutputCharacters { get; private set; }

        public Task<object> SubmitAsync(
            [Description("Overall confidence from 0 through 1.")] double confidence,
            [Description("Concise evidence-grounded fit summary.")] string summary,
            [Description("One result for every requested criterion.")]
            List<JobAnalysisCriterionSubmission> criteria)
        {
            var value = new JobAnalysisSubmission
            {
                Confidence = confidence,
                Summary = summary,
                Criteria = criteria
            };
            var outputCharacters = JsonSerializer.Serialize(value).Length;
            var validation = JobAnalysisSubmissionValidator.Validate(
                request,
                value,
                maximumOutputCharacters);

            lock (_sync)
            {
                if (Validation is null)
                {
                    Validation = validation;
                    OutputCharacters = outputCharacters;
                }
            }

            return Task.FromResult<object>(
                new
                {
                    accepted = validation.IsValid,
                    failureCode = validation.FailureCode
                });
        }
    }

    private sealed class UsageCapture
    {
        private readonly object _sync = new();

        public long? InputTokens { get; private set; }

        public long? OutputTokens { get; private set; }

        public string? Model { get; private set; }

        public void Capture(AssistantUsageEvent usageEvent)
        {
            lock (_sync)
            {
                InputTokens = Add(InputTokens, usageEvent.Data.InputTokens);
                OutputTokens = Add(OutputTokens, usageEvent.Data.OutputTokens);
                Model = usageEvent.Data.Model ?? Model;
            }
        }

        private static long? Add(long? current, long? value) =>
            value is null ? current : (current ?? 0) + value.Value;
    }

    private sealed class ErrorCapture
    {
        private readonly object _sync = new();

        public bool HasError { get; private set; }

        public int? StatusCode { get; private set; }

        public string? ErrorCode { get; private set; }

        public void Capture(SessionErrorEvent errorEvent)
        {
            lock (_sync)
            {
                HasError = true;
                StatusCode = errorEvent.Data.StatusCode;
                ErrorCode = NormalizeCode(errorEvent.Data.ErrorCode);
            }
        }

        private static string? NormalizeCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = new string(
                value.Where(character => char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')
                    .Take(96)
                    .ToArray());
            return normalized.Length == 0 ? null : normalized;
        }
    }
}

internal sealed record CopilotAvailabilityOutcome(
    bool IsAvailable,
    string StatusCode,
    string? Model);

internal sealed record CopilotRunOutcome(
    JobAnalysisStatus Status,
    JobAnalysisOutput? Output,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    int OutputCharacters,
    string? FailureCode)
{
    public static CopilotRunOutcome Failure(
        JobAnalysisStatus status,
        string failureCode,
        string? model = null,
        long? inputTokens = null,
        long? outputTokens = null,
        int outputCharacters = 0) =>
        new(
            status,
            null,
            model,
            inputTokens,
            outputTokens,
            outputCharacters,
            failureCode);
}
