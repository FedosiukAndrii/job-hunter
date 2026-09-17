using System.Reflection;
using System.Text.Json;
using JobHunter.AI.Abstractions;

namespace JobHunter.AI.Copilot;

internal static class CopilotAnalysisPromptBuilder
{
    private const string PromptTemplateResourceName =
        "JobHunter.AI.Copilot.PromptTemplates.JobAnalysis.json";

    private static readonly JobAnalysisPromptTemplate Template =
        JobAnalysisPromptTemplate.Load();

    public static string SystemMessage => Template.SystemMessage;

    private static readonly JsonSerializerOptions JsonOptions =
        JobAnalysisJson.CreateSerializerOptions();

    public static string Build(JobAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new
        {
            schemaVersion = request.SchemaVersion,
            rubricVersion = request.RubricVersion,
            criteria = request.Criteria,
            evidence = request.Evidence
        };

        return
            $"""
            BEGIN_UNTRUSTED_JOB_ANALYSIS_EVIDENCE_JSON
            {JsonSerializer.Serialize(payload, JsonOptions)}
            END_UNTRUSTED_JOB_ANALYSIS_EVIDENCE_JSON
            """;
    }

    public static string BuildCorrectiveRetry(
        JobAnalysisRequest request,
        string? failureCode)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correction = failureCode is not null
            && Template.Corrections.TryGetValue(failureCode, out var configuredCorrection)
                ? configuredCorrection
                : Template.DefaultCorrection;

        return Template.CorrectiveRetry
            .Replace("{{correction}}", correction, StringComparison.Ordinal)
            .Replace("{{evidence}}", Build(request), StringComparison.Ordinal);
    }

    private sealed class JobAnalysisPromptTemplate
    {
        public required string SystemMessage { get; init; }

        public required string CorrectiveRetry { get; init; }

        public required string DefaultCorrection { get; init; }

        public required IReadOnlyDictionary<string, string> Corrections { get; init; }

        public static JobAnalysisPromptTemplate Load()
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(PromptTemplateResourceName)
                ?? throw new InvalidOperationException(
                    "The job analysis prompt template resource is missing.");
            var template = JsonSerializer.Deserialize<JobAnalysisPromptTemplate>(stream)
                ?? throw new InvalidOperationException(
                    "The job analysis prompt template resource is invalid.");
            if (string.IsNullOrWhiteSpace(template.SystemMessage)
                || string.IsNullOrWhiteSpace(template.CorrectiveRetry)
                || string.IsNullOrWhiteSpace(template.DefaultCorrection)
                || template.Corrections.Count == 0)
            {
                throw new InvalidOperationException(
                    "The job analysis prompt template is incomplete.");
            }

            return template;
        }
    }
}
