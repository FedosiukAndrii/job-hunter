using System.Globalization;
using JobHunter.Application.Persistence;
using JobHunter.Application.Sources;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfJobIngestionStoreTests
{
    [Fact]
    public async Task SameSourceIdUpdatesOneJobAndCreatesOneRevision()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var subscriptionId = await AddSubscriptionAsync(
            host,
            SourceName.Dou,
            "dotnet");
        var run = await StartRunAsync(host, subscriptionId, At("2026-09-15T10:00:00Z"));
        var store = host.Services.GetRequiredService<IJobIngestionStore>();

        await store.PersistAsync(
            run.SourceRunId,
            subscriptionId,
            "dotnet",
            [CreateRecord(SourceName.Dou, "123", "content:v1", "raw:v1", "First description")],
            CancellationToken.None);
        var updateResult = await store.PersistAsync(
            run.SourceRunId,
            subscriptionId,
            "dotnet",
            [CreateRecord(SourceName.Dou, "123", "content:v2", "raw:v2", "Changed description")],
            CancellationToken.None);
        var repeatResult = await store.PersistAsync(
            run.SourceRunId,
            subscriptionId,
            "dotnet",
            [CreateRecord(SourceName.Dou, "123", "content:v2", "raw:v2", "Changed description")],
            CancellationToken.None);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var job = await context.Jobs.Include(candidate => candidate.Revisions).SingleAsync();

        Assert.Equal("Changed description", job.DescriptionText);
        Assert.Single(job.Revisions);
        Assert.Equal(1, updateResult.UpdatedCount);
        Assert.Equal(1, repeatResult.DuplicateCount);
        Assert.Equal(2, await context.JobObservations.CountAsync());
    }

    [Fact]
    public async Task TrackingVariationsWithoutNativeIdResolveToOneJob()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var subscriptionId = await AddSubscriptionAsync(host, SourceName.Dou, "dotnet");
        var run = await StartRunAsync(host, subscriptionId, At("2026-09-15T10:00:00Z"));
        var store = host.Services.GetRequiredService<IJobIngestionStore>();

        await store.PersistAsync(
            run.SourceRunId,
            subscriptionId,
            "dotnet",
            [
                CreateRecord(
                    SourceName.Dou,
                    sourceJobId: null,
                    "content:v1",
                    "raw:v1",
                    "Description",
                    "https://jobs.dou.ua/vacancies/123/?utm_source=first&search=dotnet")
            ],
            CancellationToken.None);
        await store.PersistAsync(
            run.SourceRunId,
            subscriptionId,
            "dotnet",
            [
                CreateRecord(
                    SourceName.Dou,
                    sourceJobId: null,
                    "content:v1",
                    "raw:v2",
                    "Description",
                    "https://jobs.dou.ua/vacancies/123/?search=dotnet&from=rss")
            ],
            CancellationToken.None);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        Assert.Equal(1, await context.Jobs.CountAsync());
        Assert.Equal(2, await context.JobObservations.CountAsync());
    }

    [Fact]
    public async Task SameFallbackFingerprintAcrossSourcesCreatesAuditableRelation()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var douSubscriptionId = await AddSubscriptionAsync(host, SourceName.Dou, "dotnet");
        var jobSpySubscriptionId = await AddSubscriptionAsync(
            host,
            SourceName.LinkedInJobSpy,
            "dotnet");
        var douRun = await StartRunAsync(
            host,
            douSubscriptionId,
            At("2026-09-15T10:00:00Z"));
        var store = host.Services.GetRequiredService<IJobIngestionStore>();
        await store.PersistAsync(
            douRun.SourceRunId,
            douSubscriptionId,
            "dotnet",
            [CreateRecord(SourceName.Dou, "dou-123", "content:dou", "raw:dou", "Build .NET APIs")],
            CancellationToken.None);
        var jobSpyRun = await StartRunAsync(
            host,
            jobSpySubscriptionId,
            At("2026-09-15T10:01:00Z"));

        await store.PersistAsync(
            jobSpyRun.SourceRunId,
            jobSpySubscriptionId,
            "dotnet",
            [
                CreateRecord(
                    SourceName.LinkedInJobSpy,
                    "linkedin-456",
                    "content:linkedin",
                    "raw:linkedin",
                    "Build .NET APIs",
                    "https://www.linkedin.com/jobs/view/456")
            ],
            CancellationToken.None);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        Assert.Equal(2, await context.Jobs.CountAsync());
        Assert.Single(await context.JobPossibleDuplicates.ToListAsync());
    }

    private static async Task<Guid> AddSubscriptionAsync(
        PersistenceTestHost host,
        SourceName source,
        string key)
    {
        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var subscription = SourceSubscription.Create(
            source,
            key,
            "{}",
            TimeSpan.FromMinutes(12),
            enabled: true,
            At("2026-09-15T09:00:00Z"));
        context.SourceSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        return subscription.Id;
    }

    private static async Task<SourceRunLease> StartRunAsync(
        PersistenceTestHost host,
        Guid subscriptionId,
        DateTimeOffset now)
    {
        var run = await host.Services
            .GetRequiredService<ISourceRunStore>()
            .TryStartAsync(
                subscriptionId,
                now,
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
        return Assert.IsType<SourceRunLease>(run);
    }

    private static JobSourceRecord CreateRecord(
        SourceName source,
        string? sourceJobId,
        string contentHash,
        string rawPayloadHash,
        string description,
        string url = "https://jobs.dou.ua/vacancies/123/") =>
        new()
        {
            Source = source,
            SourceJobId = sourceJobId is null ? null : NativeSourceId.Create(sourceJobId),
            SourceUrl = new Uri(url),
            CanonicalUrl = CanonicalJobUrl.Create(url),
            SourceGuid = sourceJobId,
            Title = "Senior .NET Engineer",
            Company = "Example",
            DescriptionHtml = $"<p>{description}</p>",
            DescriptionText = description,
            Locations = ["Kyiv"],
            WorkplaceMode = WorkplaceMode.Remote,
            EmploymentType = EmploymentType.FullTime,
            Seniority = "Senior",
            Skills = [".NET", "C#"],
            Categories = [".NET"],
            PublishedAtUtc = At("2026-09-15T09:00:00Z"),
            PublishedAtPrecision = PublishedAtPrecision.DateTime,
            ParserVersion = "test-v1",
            ContentHash = contentHash,
            RawPayloadHash = rawPayloadHash,
            RetrievedAtUtc = At("2026-09-15T10:00:00Z")
        };

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
