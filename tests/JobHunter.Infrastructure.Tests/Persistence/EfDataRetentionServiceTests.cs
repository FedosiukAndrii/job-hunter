using JobHunter.Application.Persistence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfDataRetentionServiceTests
{
    [Fact]
    public async Task CleanupRemovesExpiredHistoryAndRetainsOutboxDeduplicationKey()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var old = DateTimeOffset.Parse(
            "2026-07-01T10:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        await AddExpiredRecordsAsync(host, old);
        var service = host.Services.GetRequiredService<IDataRetentionService>();

        var summary = await service.CleanupAsync(
            old.AddDays(31),
            CancellationToken.None);

        Assert.Equal(1, summary.SourceRunCount);
        Assert.Equal(1, summary.ObservationCount);
        Assert.Equal(1, summary.DeliveryAttemptCount);
        Assert.Equal(1, summary.ScrubbedOutboxCount);
        Assert.Equal(1, summary.ApplicationEventCount);

        await using var context = await host.Services
            .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
            .CreateDbContextAsync();
        Assert.Empty(await context.SourceRuns.ToListAsync());
        Assert.Empty(await context.JobObservations.ToListAsync());
        Assert.Empty(await context.DeliveryAttempts.ToListAsync());
        var outbox = await context.NotificationOutbox.SingleAsync();
        Assert.Equal(OutboxStatus.Sent, outbox.Status);
        Assert.Equal("{}", outbox.PayloadJson);
        Assert.Empty(await context.ApplicationEvents.ToListAsync());
    }

    private static async Task AddExpiredRecordsAsync(
        PersistenceTestHost host,
        DateTimeOffset old)
    {
        await using var context = await host.Services
            .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
            .CreateDbContextAsync();
        var subscription = SourceSubscription.Create(
            SourceName.Dou,
            "retention",
            """{"endpoint":"https://jobs.dou.ua/feed","maximumItems":10}""",
            TimeSpan.FromMinutes(12),
            true,
            old);
        var run = SourceRun.Start(
            subscription.Id,
            old,
            TimeSpan.FromMinutes(5));
        var leaseToken = run.LeaseToken;
        run.Complete(
            leaseToken,
            SourceRunStatus.Succeeded,
            old.AddMinutes(1),
            1,
            1,
            0,
            0,
            null,
            null,
            null,
            null);
        var job = CreateJob(old);
        var observation = JobObservation.Create(
            job.Id,
            run.Id,
            subscription.Id,
            "https://jobs.dou.ua/vacancies/retention/",
            "retention",
            "test-v1",
            "sha256:content",
            "sha256:raw",
            null,
            old);
        var outbox = NotificationOutbox.Create(
            "telegram-test",
            job.Id,
            1,
            """{"title":"private job text"}""",
            old);
        var attempt = outbox.Lease(old, TimeSpan.FromMinutes(1));
        outbox.MarkSent(
            outbox.LeaseToken!,
            attempt.Id,
            "message-1",
            old.AddSeconds(1));

        context.SourceSubscriptions.Add(subscription);
        context.SourceRuns.Add(run);
        context.Jobs.Add(job);
        context.JobObservations.Add(observation);
        context.NotificationOutbox.Add(outbox);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ApplicationEvents
                (Id, EventType, Severity, Message, PropertiesJson, CreatedAtUtc)
            VALUES
                ({Guid.NewGuid()}, {"RetentionFixture"}, {"Information"},
                 {"Safe fixture event"}, {null}, {old})
            """);
    }

    private static JobHunter.Domain.Jobs.Job CreateJob(DateTimeOffset now) =>
        JobHunter.Domain.Jobs.Job.Create(
            SourceName.Dou,
            "retention",
            "https://jobs.dou.ua/vacancies/retention/",
            "https://jobs.dou.ua/vacancies/retention/",
            null,
            "Senior .NET Engineer",
            "Example",
            string.Empty,
            ".NET role",
            "[]",
            WorkplaceMode.Remote,
            EmploymentType.FullTime,
            "Senior",
            "[\".NET\"]",
            "[\".NET\"]",
            null,
            null,
            null,
            CompensationPeriod.Unknown,
            now,
            PublishedAtPrecision.DateTime,
            "sha256:content",
            "v1:fingerprint",
            1,
            now);
}
