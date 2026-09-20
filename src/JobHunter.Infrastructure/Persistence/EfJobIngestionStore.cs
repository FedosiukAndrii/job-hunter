using System.Data;
using System.Text.Json;
using JobHunter.Application.Persistence;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class EfJobIngestionStore(IDbContextFactory<JobHunterDbContext> contextFactory)
    : IJobIngestionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobIngestionResult> PersistAsync(
        Guid sourceRunId,
        Guid sourceSubscriptionId,
        string? queryId,
        IReadOnlyCollection<JobSourceRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var runExists = await context.SourceRuns.AnyAsync(
            run => run.Id == sourceRunId
                && run.SourceSubscriptionId == sourceSubscriptionId
                && run.Status == Domain.Sources.SourceRunStatus.Running,
            cancellationToken);
        if (!runExists)
        {
            throw new InvalidOperationException("Jobs can only be persisted for an active source run.");
        }

        var createdCount = 0;
        var updatedCount = 0;
        var duplicateCount = 0;
        var persistedJobs = new List<PersistedJob>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jobKey = JobKey.Create(record.Source, record.SourceJobId, record.CanonicalUrl).Value;
            if (!seenKeys.Add(jobKey))
            {
                duplicateCount++;
                continue;
            }

            var sourceJobId = record.SourceJobId?.Value;
            var canonicalUrl = record.CanonicalUrl.ToString();
            var job = sourceJobId is null
                ? await context.Jobs
                    .Include(existing => existing.Revisions)
                    .SingleOrDefaultAsync(
                        existing => existing.Source == record.Source
                            && existing.SourceJobId == null
                            && existing.CanonicalUrl == canonicalUrl,
                        cancellationToken)
                : await context.Jobs
                    .Include(existing => existing.Revisions)
                    .SingleOrDefaultAsync(
                        existing => existing.Source == record.Source
                            && existing.SourceJobId == sourceJobId,
                        cancellationToken);

            var locationsJson = Serialize(record.Locations);
            var skillsJson = Serialize(record.Skills);
            var categoriesJson = Serialize(record.Categories);
            var fingerprint = JobFingerprint.Create(
                record.Title,
                record.Company,
                record.WorkplaceMode,
                record.Locations,
                record.EmploymentType,
                record.DescriptionText);

            if (job is null)
            {
                job = JobHunter.Domain.Jobs.Job.Create(
                    record.Source,
                    sourceJobId,
                    canonicalUrl,
                    record.SourceUrl.AbsoluteUri,
                    record.ApplicationUrl?.AbsoluteUri,
                    record.Title,
                    record.Company,
                    record.DescriptionHtml,
                    record.DescriptionText,
                    locationsJson,
                    record.WorkplaceMode,
                    record.EmploymentType,
                    record.Seniority,
                    skillsJson,
                    categoriesJson,
                    record.CompensationMinimum,
                    record.CompensationMaximum,
                    record.CompensationCurrency,
                    record.CompensationPeriod,
                    record.PublishedAtUtc,
                    record.PublishedAtPrecision,
                    record.ContentHash,
                    fingerprint,
                    JobFingerprint.Version,
                    record.RetrievedAtUtc);
                context.Jobs.Add(job);
                createdCount++;

            }
            else
            {
                var previousSnapshotJson = SerializeSnapshot(job);
                var revision = job.ApplyObservation(
                    record.SourceUrl.AbsoluteUri,
                    record.ApplicationUrl?.AbsoluteUri,
                    record.Title,
                    record.Company,
                    record.DescriptionHtml,
                    record.DescriptionText,
                    locationsJson,
                    record.WorkplaceMode,
                    record.EmploymentType,
                    record.Seniority,
                    skillsJson,
                    categoriesJson,
                    record.CompensationMinimum,
                    record.CompensationMaximum,
                    record.CompensationCurrency,
                    record.CompensationPeriod,
                    record.PublishedAtUtc,
                    record.PublishedAtPrecision,
                    record.ContentHash,
                    fingerprint,
                    JobFingerprint.Version,
                    previousSnapshotJson,
                    record.RetrievedAtUtc);

                if (revision is null)
                {
                    duplicateCount++;
                }
                else
                {
                    context.JobRevisions.Add(revision);
                    updatedCount++;
                }
            }

            await AddCrossSourcePossibleDuplicatesAsync(
                context,
                job,
                record.RetrievedAtUtc,
                cancellationToken);

            var observationExists = await context.JobObservations.AnyAsync(
                observation => observation.SourceRunId == sourceRunId
                    && observation.JobId == job.Id
                    && observation.RawPayloadHash == record.RawPayloadHash,
                cancellationToken);
            if (!observationExists)
            {
                context.JobObservations.Add(
                    JobObservation.Create(
                        job.Id,
                        sourceRunId,
                        sourceSubscriptionId,
                        record.SourceUrl.AbsoluteUri,
                        record.SourceGuid,
                        record.ParserVersion,
                        record.ContentHash,
                        record.RawPayloadHash,
                        queryId,
                        record.RetrievedAtUtc));
            }

            persistedJobs.Add(
                new PersistedJob(
                    job.Id,
                    job.Revisions.Count,
                    record));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new JobIngestionResult(
            records.Count,
            createdCount,
            updatedCount,
            duplicateCount,
            persistedJobs);
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static async Task AddCrossSourcePossibleDuplicatesAsync(
        JobHunterDbContext context,
        JobHunter.Domain.Jobs.Job job,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (job.PublishedAtUtc is null)
        {
            return;
        }

        if (job.Source != SourceName.Dou
            && job.Source != SourceName.LinkedInJobSpy)
        {
            return;
        }

        var counterpartSource = job.Source == SourceName.Dou
            ? SourceName.LinkedInJobSpy
            : SourceName.Dou;
        var publishedAtMatchWindow =
            CrossSourceJobDuplicateMatcher.GetPublishedAtMatchWindow(
                job.PublishedAtUtc.Value);
        var earliestPublishedAtUnixTimeSeconds =
            publishedAtMatchWindow.Earliest.ToUnixTimeSeconds();
        var latestPublishedAtUnixTimeSeconds =
            publishedAtMatchWindow.Latest.ToUnixTimeSeconds();

        var candidates = await context.Jobs
            .AsNoTracking()
            .Where(existing => existing.Source == counterpartSource
                && existing.PublishedAtUnixTimeSeconds >= earliestPublishedAtUnixTimeSeconds
                && existing.PublishedAtUnixTimeSeconds <= latestPublishedAtUnixTimeSeconds)
            .Select(
                existing => new
                {
                    existing.Id,
                    existing.Company,
                    existing.Title,
                    existing.PublishedAtUtc
                })
            .ToListAsync(cancellationToken);
        var existingRelations = await context.JobPossibleDuplicates
            .Where(pair => pair.FirstJobId == job.Id || pair.SecondJobId == job.Id)
            .ToListAsync(cancellationToken);
        var relationByRelatedJobId = existingRelations.ToDictionary(
            pair => pair.FirstJobId == job.Id ? pair.SecondJobId : pair.FirstJobId);
        var matchKey = CrossSourceJobDuplicateMatcher.CreateMatchKey(job.Company, job.Title);

        foreach (var candidate in candidates)
        {
            if (!CrossSourceJobDuplicateMatcher.IsMatch(
                    job.Company,
                    job.Title,
                    job.PublishedAtUtc,
                    candidate.Company,
                    candidate.Title,
                    candidate.PublishedAtUtc))
            {
                continue;
            }

            if (relationByRelatedJobId.TryGetValue(candidate.Id, out var existingRelation))
            {
                existingRelation.ApplyCompanyTitlePublishedAtV2(matchKey);
                continue;
            }

            context.JobPossibleDuplicates.Add(
                JobPossibleDuplicate.CreateCompanyTitlePublishedAtV2(
                    job.Id,
                    candidate.Id,
                    matchKey,
                    now));
        }
    }

    private static string SerializeSnapshot(JobHunter.Domain.Jobs.Job job) =>
        Serialize(
            new
            {
                job.Title,
                job.Company,
                job.DescriptionHtml,
                job.DescriptionText,
                job.LocationsJson,
                job.WorkplaceMode,
                job.EmploymentType,
                job.Seniority,
                job.SkillsJson,
                job.CategoriesJson,
                job.CompensationMinimum,
                job.CompensationMaximum,
                job.CompensationCurrency,
                job.CompensationPeriod,
                job.PublishedAtUtc,
                job.PublishedAtPrecision,
                job.SourceUrl,
                job.ApplicationUrl
            });
}
