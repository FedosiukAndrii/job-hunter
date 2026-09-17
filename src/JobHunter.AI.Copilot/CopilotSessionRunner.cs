using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GitHub.Copilot;
using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using JobHunter.Application.Security;
using JobHunter.Application.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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

internal sealed partial class CopilotSessionRunner(
    IAppDataDirectory appDataDirectory,
    ISecretReader secretReader,
    IOptions<CopilotOptions> options,
    ILogger<CopilotSessionRunner> logger)
    : ICopilotSessionRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public async Task<CopilotAvailabilityOutcome> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var configuredOptions = options.Value;
        var configuredModel = string.IsNullOrWhiteSpace(configuredOptions.Model)
            ? "auto"
            : configuredOptions.Model.Trim();

        try
        {
            var clientOptions = CreateClientOptions(out var workingDirectory);

            await using var client = new CopilotClient(clientOptions);
            await client.StartAsync(cancellationToken);
            var auth = await client.GetAuthStatusAsync(cancellationToken);
            if (!auth.IsAuthenticated)
            {
                return new CopilotAvailabilityOutcome(
                    false,
                    "CopilotAuthenticationUnavailable",
                    configuredModel);
            }

            var models = await client.ListModelsAsync(cancellationToken);
            if (models.Count == 0)
            {
                return new CopilotAvailabilityOutcome(
                    false,
                    "CopilotModelsUnavailable",
                    configuredModel);
            }

            if (!string.Equals(configuredModel, "auto", StringComparison.OrdinalIgnoreCase)
                && !models.Any(
                    model => string.Equals(
                        model.Id,
                        configuredModel,
                        StringComparison.OrdinalIgnoreCase)))
            {
                if (HasAutoOnlyCatalog(models))
                {
                    ModelCatalogIsLimitedToAuto(configuredModel);
                }
                else
                {
                    ModelNotPresentInCatalog(
                        configuredModel,
                        models.Count,
                        DescribeModelIds(models));
                    return new CopilotAvailabilityOutcome(
                        false,
                        "CopilotModelUnavailable",
                        configuredModel);
                }
            }

            return await ProbeModelSessionAsync(
                client,
                configuredOptions,
                configuredModel,
                workingDirectory,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AvailabilityCheckFailed(configuredModel, exception.GetType().Name);
            return new CopilotAvailabilityOutcome(
                false,
                "CopilotAvailabilityCheckFailed",
                configuredModel);
        }
    }

    private async Task<CopilotAvailabilityOutcome> ProbeModelSessionAsync(
        CopilotClient client,
        CopilotOptions configuredOptions,
        string configuredModel,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        CopilotSession session;
        try
        {
            session = await client.CreateSessionAsync(
                CopilotSessionConfigurationFactory.CreateAvailabilityProbe(
                    configuredOptions,
                    workingDirectory),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ModelSessionProbeFailed(configuredModel, exception.GetType().Name);
            return new CopilotAvailabilityOutcome(
                false,
                "CopilotModelAvailabilityProbeFailed",
                configuredModel);
        }

        var cleanupSucceeded = await DisposeSessionAsync(session);
        cleanupSucceeded = await DeleteSessionAsync(client, session.SessionId)
            && cleanupSucceeded;
        if (!cleanupSucceeded)
        {
            ModelSessionProbeCleanupFailed(configuredModel);
            return new CopilotAvailabilityOutcome(
                false,
                "CopilotModelAvailabilityProbeCleanupFailed",
                configuredModel);
        }

        return new CopilotAvailabilityOutcome(true, "Ready", configuredModel);
    }

    private static bool HasAutoOnlyCatalog(IList<ModelInfo> models) =>
        models.Count == 1
        && string.Equals(models[0].Id, "auto", StringComparison.OrdinalIgnoreCase);

    private static string DescribeModelIds(IList<ModelInfo> models) =>
        string.Join(
            ",",
            models
                .Select(model => model.Id)
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .Select(modelId => modelId!.Length <= 96 ? modelId : modelId[..96]));

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Configured Copilot model {ConfiguredModel} was not found in the SDK catalog: count={ModelCount}, ids={ModelIds}.")]
    private partial void ModelNotPresentInCatalog(
        string configuredModel,
        int modelCount,
        string modelIds);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Copilot SDK catalog only exposed auto; validating configured model {ConfiguredModel} by creating a restricted session.")]
    private partial void ModelCatalogIsLimitedToAuto(string configuredModel);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Copilot availability check failed for model {ConfiguredModel} with {ExceptionType}.")]
    private partial void AvailabilityCheckFailed(
        string configuredModel,
        string exceptionType);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Copilot model availability probe failed for model {ConfiguredModel} with {ExceptionType}.")]
    private partial void ModelSessionProbeFailed(
        string configuredModel,
        string exceptionType);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Copilot model availability probe cleanup failed for model {ConfiguredModel}.")]
    private partial void ModelSessionProbeCleanupFailed(string configuredModel);

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
            Connection = RuntimeConnection.ForStdio(),
            BaseDirectory = baseDirectory,
            WorkingDirectory = workingDirectory,
            LogLevel = logger.IsEnabled(LogLevel.Debug)
                ? CopilotLogLevel.Debug
                : CopilotLogLevel.None,
            Logger = logger,
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
            (Func<
                double,
                string,
                List<string>,
                List<string>,
                List<JobAnalysisCriterionSubmission>,
                Task<object>>)
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
                null,
                usage.AiCredits);
        }

        if (submission.Validation is not null)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.InvalidOutput,
                submission.Validation.FailureCode ?? "InvalidStructuredOutput",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens,
                submission.OutputCharacters,
                usage.AiCredits);
        }

        if (error.StatusCode == 429)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.TransientFailure,
                "CopilotRateLimited",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens,
                aiCredits: usage.AiCredits);
        }

        if (error.StatusCode is 401 or 403)
        {
            return CopilotRunOutcome.Failure(
                JobAnalysisStatus.Refused,
                "CopilotAccessDenied",
                usage.Model ?? assistantModel,
                usage.InputTokens,
                usage.OutputTokens,
                aiCredits: usage.AiCredits);
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
                usage.OutputTokens,
                aiCredits: usage.AiCredits);
        }

        return CopilotRunOutcome.Failure(
            JobAnalysisStatus.InvalidOutput,
            "MissingStructuredOutput",
            usage.Model ?? assistantModel,
            usage.InputTokens,
            usage.OutputTokens,
            aiCredits: usage.AiCredits);
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
            [Description("One or two concise evidence-grounded job-fit strengths.")]
            List<string> strengths,
            [Description("One or two concise evidence-grounded concerns or mismatches; use an empty list when none are known.")]
            List<string> concerns,
            [Description("One result for every requested criterion.")]
            List<JobAnalysisCriterionSubmission> criteria)
        {
            var value = new JobAnalysisSubmission
            {
                Confidence = confidence,
                Summary = summary,
                Strengths = strengths,
                Concerns = concerns,
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

        public double? AiCredits { get; private set; }

        public void Capture(AssistantUsageEvent usageEvent)
        {
            lock (_sync)
            {
                InputTokens = Add(InputTokens, usageEvent.Data.InputTokens);
                OutputTokens = Add(OutputTokens, usageEvent.Data.OutputTokens);
                AiCredits = Add(
                    AiCredits,
                    ToAiCredits(usageEvent.Data.CopilotUsage?.TotalNanoAiu));
                Model = usageEvent.Data.Model ?? Model;
            }
        }

        private static long? Add(long? current, long? value) =>
            value is null ? current : (current ?? 0) + value.Value;

        private static double? Add(double? current, double? value) =>
            value is null ? current : (current ?? 0) + value.Value;

        private static double? ToAiCredits(double? totalNanoAiu) =>
            totalNanoAiu is null ? null : totalNanoAiu.Value / 1_000_000_000d;
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
    string? FailureCode,
    double? AiCredits = null)
{
    public static CopilotRunOutcome Failure(
        JobAnalysisStatus status,
        string failureCode,
        string? model = null,
        long? inputTokens = null,
        long? outputTokens = null,
        int outputCharacters = 0,
        double? aiCredits = null) =>
        new(
            status,
            null,
            model,
            inputTokens,
            outputTokens,
            outputCharacters,
            failureCode,
            aiCredits);
}
