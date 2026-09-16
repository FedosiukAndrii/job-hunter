using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Abstractions;
using JobHunter.Application.Evaluation;
using JobHunter.Domain.AI;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfAiAnalysisStore(
    IDbContextFactory<JobHunterDbContext> contextFactory)
    : IAiAnalysisStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<StoredJobAnalysis?> GetAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        string provider,
        string schemaVersion,
        string rubricVersion,
        CancellationToken cancellationToken)
    {
        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var analysis = await context.AiAnalyses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.JobId == jobId
                    && entity.CandidateProfileSnapshotId == candidateProfileSnapshotId
                    && entity.JobRevisionNumber == jobRevisionNumber
                    && entity.Provider == provider
                    && entity.SchemaVersion == schemaVersion
                    && entity.RubricVersion == rubricVersion,
                cancellationToken);
        if (analysis?.ResultJson is null)
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<JobAnalysisResult>(
            analysis.ResultJson,
            JsonOptions)
            ?? throw new InvalidDataException(
                $"Persisted AI analysis '{analysis.Id}' has an empty result.");
        return new StoredJobAnalysis(result, analysis.CreatedAtUtc);
    }

    public async Task SaveAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        JobAnalysisResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.AiAnalyses.SingleOrDefaultAsync(
            entity =>
                entity.JobId == jobId
                && entity.CandidateProfileSnapshotId == candidateProfileSnapshotId
                && entity.JobRevisionNumber == jobRevisionNumber
                && entity.Provider == result.Provider
                && entity.SchemaVersion == result.SchemaVersion
                && entity.RubricVersion == result.RubricVersion,
            cancellationToken);
        var warningCode = result.FailureCode
            ?? (result.Warnings.Count > 0 ? result.Warnings[0] : null);
        if (warningCode?.Length > 128)
        {
            warningCode = warningCode[..128];
        }

        var resultJson = JsonSerializer.Serialize(result, JsonOptions);
        var usageJson = JsonSerializer.Serialize(result.Usage, JsonOptions);
        if (existing is null)
        {
            context.AiAnalyses.Add(
                AiAnalysis.Create(
                    jobId,
                    candidateProfileSnapshotId,
                    jobRevisionNumber,
                    result.Provider,
                    result.Model,
                    result.Status.ToString(),
                    result.SchemaVersion,
                    result.RubricVersion,
                    resultJson,
                    usageJson,
                    warningCode,
                    now));
        }
        else
        {
            existing.ReplaceResult(
                result.Model,
                result.Status.ToString(),
                resultJson,
                usageJson,
                warningCode,
                now);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
