using System.Text.Json;
using JobHunter.Application.Evaluation;
using JobHunter.Domain.Evaluation;
using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfRuleEvaluationStore(
    IDbContextFactory<JobHunterDbContext> contextFactory)
    : IRuleEvaluationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> SaveAsync(
        Guid jobId,
        Guid candidateProfileSnapshotId,
        int jobRevisionNumber,
        DeterministicEvaluation evaluation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentOutOfRangeException.ThrowIfNegative(jobRevisionNumber);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingId = await context.RuleEvaluations
            .Where(candidate => candidate.JobId == jobId
                && candidate.CandidateProfileSnapshotId == candidateProfileSnapshotId
                && candidate.JobRevisionNumber == jobRevisionNumber
                && candidate.RubricVersion == evaluation.RubricVersion)
            .Select(candidate => (Guid?)candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var entity = RuleEvaluation.Create(
            jobId,
            candidateProfileSnapshotId,
            jobRevisionNumber,
            evaluation.RubricVersion,
            evaluation.PassedHardFilters,
            JsonSerializer.Serialize(evaluation.HardFilters, JsonOptions),
            evaluation.Explanation,
            now);
        context.RuleEvaluations.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
