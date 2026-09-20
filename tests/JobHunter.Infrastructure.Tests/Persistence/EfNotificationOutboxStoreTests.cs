using JobHunter.Application.Notifications;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfNotificationOutboxStoreTests
{
    [Fact]
    public async Task TryLeaseNextReadsLegacyNotificationPayloadWithoutNewDisplayFields()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        const string legacyPayload =
            """
            {
              "title":"Senior .NET Engineer",
              "company":"Example",
              "locations":["Remote"],
              "workplaceMode":1,
              "score":90,
              "summary":"Legacy summary.",
              "compensationMinimum":5000,
              "compensationMaximum":6000,
              "compensationCurrency":"USD",
              "compensationPeriod":2,
              "publishedAtUtc":"1970-01-01T00:00:00+00:00",
              "canonicalUrl":"https://jobs.dou.ua/vacancies/123456/"
            }
            """;
        await using (var context = await CreateContextAsync(host))
        {
            context.NotificationOutbox.Add(NotificationOutbox.Create(
                "telegram-legacy",
                jobId,
                1,
                legacyPayload,
                now));
            await context.SaveChangesAsync();
        }

        var lease = await host.Services
            .GetRequiredService<INotificationOutboxStore>()
            .TryLeaseNextAsync(now, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Empty(lease.Payload.Strengths);
        Assert.Empty(lease.Payload.Concerns);
    }

    [Fact]
    public async Task EnqueueUsesDurableNotificationKey()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        var intent = CreateIntent(jobId);

        var first = await store.EnqueueAsync(intent, now, CancellationToken.None);
        var second = await store.EnqueueAsync(
            intent with { Payload = intent.Payload with { Score = 99 } },
            now.AddSeconds(1),
            CancellationToken.None);

        Assert.Equal(NotificationEnqueueOutcome.Created, first);
        Assert.Equal(NotificationEnqueueOutcome.AlreadyExists, second);

        await using var context = await CreateContextAsync(host);
        Assert.Equal(1, await context.NotificationOutbox.CountAsync());
    }

    [Fact]
    public async Task EnqueueSuppressesPossibleDuplicateOnlyAfterRelatedJobWasSentToSameDestination()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var sentJobId = await AddJobAsync(host, now);
        var duplicateJobId = await AddJobAsync(
            host,
            now,
            SourceName.LinkedInJobSpy,
            "linkedin-456");
        await using (var context = await CreateContextAsync(host))
        {
            context.JobPossibleDuplicates.Add(
                JobPossibleDuplicate.CreateCompanyTitlePublishedAtV2(
                    sentJobId,
                    duplicateJobId,
                    CrossSourceJobDuplicateMatcher.CreateMatchKey(
                        "Example",
                        "Senior .NET Engineer"),
                    now));
            await context.SaveChangesAsync();
        }

        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        Assert.Equal(
            NotificationEnqueueOutcome.Created,
            await store.EnqueueAsync(
                CreateIntent(sentJobId),
                now,
                CancellationToken.None));
        var lease = await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(lease);
        await store.CompleteAsync(
            lease,
            NotificationSendResult.Sent("telegram-message-1"),
            null,
            now.AddSeconds(1),
            CancellationToken.None);

        Assert.Equal(
            NotificationEnqueueOutcome.Created,
            await store.EnqueueAsync(
                CreateIntent(duplicateJobId) with
                {
                    DestinationId = "telegram-other",
                    SuppressPossibleDuplicateNotifications = true
                },
                now.AddSeconds(2),
                CancellationToken.None));
        Assert.Equal(
            NotificationEnqueueOutcome.PossibleDuplicateAlreadySent,
            await store.EnqueueAsync(
                CreateIntent(duplicateJobId) with
                {
                    SuppressPossibleDuplicateNotifications = true
                },
                now.AddSeconds(2),
                CancellationToken.None));

        await using var verificationContext = await CreateContextAsync(host);
        Assert.Equal(2, await verificationContext.NotificationOutbox.CountAsync());
    }

    [Fact]
    public async Task TryLeaseNextSuppressesPendingPossibleDuplicateOnceRelatedJobWasSent()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var firstJobId = await AddJobAsync(host, now);
        var duplicateJobId = await AddJobAsync(
            host,
            now,
            SourceName.LinkedInJobSpy,
            "linkedin-456");
        await using (var context = await CreateContextAsync(host))
        {
            context.JobPossibleDuplicates.Add(
                JobPossibleDuplicate.CreateCompanyTitlePublishedAtV2(
                    firstJobId,
                    duplicateJobId,
                    CrossSourceJobDuplicateMatcher.CreateMatchKey(
                        "Example",
                        "Senior .NET Engineer"),
                    now));
            await context.SaveChangesAsync();
        }

        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        await store.EnqueueAsync(
            CreateIntent(firstJobId) with
            {
                SuppressPossibleDuplicateNotifications = true
            },
            now,
            CancellationToken.None);
        await store.EnqueueAsync(
            CreateIntent(duplicateJobId) with
            {
                SuppressPossibleDuplicateNotifications = true
            },
            now.AddSeconds(1),
            CancellationToken.None);

        var firstLease = await store.TryLeaseNextAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(firstLease);
        Assert.Equal(
            "https://jobs.dou.ua/vacancies/123456/",
            firstLease.Payload.CanonicalUrl);
        await store.CompleteAsync(
            firstLease,
            NotificationSendResult.Sent("telegram-message-1"),
            null,
            now.AddSeconds(3),
            CancellationToken.None);

        Assert.Null(await store.TryLeaseNextAsync(
            now.AddSeconds(4),
            TimeSpan.FromMinutes(1),
            CancellationToken.None));

        await using var verificationContext = await CreateContextAsync(host);
        var suppressed = await verificationContext.NotificationOutbox
            .Include(outbox => outbox.DeliveryAttempts)
            .SingleAsync(outbox => outbox.JobId == duplicateJobId);
        Assert.Equal(OutboxStatus.PermanentFailure, suppressed.Status);
        Assert.Contains(
            suppressed.DeliveryAttempts,
            attempt => attempt.Outcome == "SuppressedPossibleDuplicate"
                && attempt.ErrorCode == "PossibleDuplicateAlreadySent");
    }

    [Fact]
    public async Task EnqueueSuppressesWhenDestinationQueueIsFull()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        await using (var context = await CreateContextAsync(host))
        {
            context.NotificationOutbox.AddRange(
                Enumerable.Range(1, 1_000)
                    .Select(
                        version => NotificationOutbox.Create(
                            "telegram-capacity",
                            jobId,
                            version,
                            "{}",
                            now)));
            await context.SaveChangesAsync();
        }

        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        var outcome = await store.EnqueueAsync(
            CreateIntent(jobId) with
            {
                DestinationId = "telegram-capacity",
                NotificationVersion = 1_001
            },
            now,
            CancellationToken.None);

        Assert.Equal(NotificationEnqueueOutcome.QueueFull, outcome);
        await using var verificationContext = await CreateContextAsync(host);
        Assert.Equal(
            1_000,
            await verificationContext.NotificationOutbox.CountAsync());
    }

    [Fact]
    public async Task RateLimitedDeliveryIsRequeuedWithoutChangingItsKey()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        var intent = CreateIntent(jobId);
        await store.EnqueueAsync(intent, now, CancellationToken.None);
        await store.EnqueueAsync(
            intent with { NotificationVersion = 2 },
            now,
            CancellationToken.None);
        var lease = await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(lease);
        var retryAtUtc = now.AddSeconds(30);

        await store.CompleteAsync(
            lease,
            NotificationSendResult.Retry(
                "TelegramRateLimited",
                retryAtUtc,
                rateLimited: true),
            retryAtUtc,
            now.AddSeconds(1),
            CancellationToken.None);
        Assert.Equal(
            NotificationEnqueueOutcome.Created,
            await store.EnqueueAsync(
                intent with { NotificationVersion = 3 },
                now.AddSeconds(2),
                CancellationToken.None));

        Assert.Null(
            await store.TryLeaseNextAsync(
                now.AddSeconds(29),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
        Assert.NotNull(
            await store.TryLeaseNextAsync(
                retryAtUtc,
                TimeSpan.FromMinutes(1),
                CancellationToken.None));

        await using var context = await CreateContextAsync(host);
        var outbox = await context.NotificationOutbox
            .Include(item => item.DeliveryAttempts)
            .SingleAsync(item => item.Status == OutboxStatus.Leased);
        Assert.Equal(2, outbox.AttemptCount);
        Assert.Equal("RateLimited", outbox.DeliveryAttempts.Single(
            attempt => attempt.AttemptNumber == 1).Outcome);
        Assert.All(
            await context.NotificationOutbox.ToListAsync(),
            item => Assert.True(item.NextAttemptAtUtc >= retryAtUtc));
        var destination = await context.NotificationDestinationStates.SingleAsync();
        Assert.Equal(retryAtUtc, destination.RateLimitedUntilUtc);
    }

    [Fact]
    public async Task FinalRateLimitedAttemptDefersRowsEnqueuedAfterCompletion()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var retryAtUtc = now.AddMinutes(2);
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        var intent = CreateIntent(jobId);
        await store.EnqueueAsync(intent, now, CancellationToken.None);
        var lease = await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(lease);

        await store.CompleteAsync(
            lease,
            NotificationSendResult.PermanentFailure("RetryLimitExceeded"),
            retryAtUtc,
            now.AddSeconds(1),
            CancellationToken.None);
        var restartedStore = new EfNotificationOutboxStore(
            host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>());
        Assert.Equal(
            NotificationEnqueueOutcome.Created,
            await restartedStore.EnqueueAsync(
                intent with { NotificationVersion = 2 },
                now.AddSeconds(2),
                CancellationToken.None));

        Assert.Null(
            await restartedStore.TryLeaseNextAsync(
                retryAtUtc.AddTicks(-1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
        Assert.NotNull(
            await restartedStore.TryLeaseNextAsync(
                retryAtUtc,
                TimeSpan.FromMinutes(1),
                CancellationToken.None));

        await using var context = await CreateContextAsync(host);
        var destination = await context.NotificationDestinationStates.SingleAsync();
        Assert.Equal(retryAtUtc, destination.RateLimitedUntilUtc);
    }

    [Fact]
    public async Task UnknownDeliveryIsNotAutomaticallyRetried()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        await store.EnqueueAsync(CreateIntent(jobId), now, CancellationToken.None);
        var lease = await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(lease);

        await store.CompleteAsync(
            lease,
            NotificationSendResult.Unknown("TelegramSendTimeout"),
            null,
            now.AddSeconds(1),
            CancellationToken.None);

        Assert.Null(
            await store.TryLeaseNextAsync(
                now.AddHours(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
        await using var context = await CreateContextAsync(host);
        var outbox = await context.NotificationOutbox.SingleAsync();
        Assert.Equal(OutboxStatus.Unknown, outbox.Status);
    }

    [Fact]
    public async Task ExpiredDeliveryLeaseBecomesUnknown()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        await store.EnqueueAsync(CreateIntent(jobId), now, CancellationToken.None);
        await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var recovered = await store.RecoverExpiredLeasesAsync(
            now.AddSeconds(6),
            CancellationToken.None);

        Assert.Equal(1, recovered);
        await using var context = await CreateContextAsync(host);
        var outbox = await context.NotificationOutbox
            .Include(item => item.DeliveryAttempts)
            .SingleAsync();
        Assert.Equal(OutboxStatus.Unknown, outbox.Status);
        Assert.Equal("Unknown", Assert.Single(outbox.DeliveryAttempts).Outcome);
        Assert.Equal("LeaseExpired", Assert.Single(outbox.DeliveryAttempts).ErrorCode);
    }

    [Fact]
    public async Task PermanentDestinationFailureSuppressesFutureIntents()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.UnixEpoch;
        var jobId = await AddJobAsync(host, now);
        var store = host.Services.GetRequiredService<INotificationOutboxStore>();
        var intent = CreateIntent(jobId);
        await store.EnqueueAsync(intent, now, CancellationToken.None);
        await store.EnqueueAsync(
            intent with { NotificationVersion = 2 },
            now,
            CancellationToken.None);
        var lease = await store.TryLeaseNextAsync(
            now,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(lease);

        await store.CompleteAsync(
            lease,
            NotificationSendResult.PermanentFailure(
                "TelegramHttp403",
                disableDestination: true),
            null,
            now.AddSeconds(1),
            CancellationToken.None);
        var enqueueOutcome = await store.EnqueueAsync(
            intent with { NotificationVersion = 2 },
            now.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(NotificationEnqueueOutcome.DestinationDisabled, enqueueOutcome);
        Assert.Null(
            await store.TryLeaseNextAsync(
                now.AddMinutes(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
        var destinationStateStore =
            host.Services.GetRequiredService<INotificationDestinationStateStore>();
        var state = await destinationStateStore.GetAsync(
            " telegram-test ",
            CancellationToken.None);
        Assert.False(state?.IsEnabled);
        Assert.Equal("TelegramHttp403", state?.FailureCode);

        await destinationStateStore.SetDestinationEnabledAsync(
            "telegram-test",
            enabled: true,
            failureCode: null,
            now.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(
            NotificationEnqueueOutcome.Created,
            await store.EnqueueAsync(
                intent with { NotificationVersion = 3 },
                now.AddMinutes(2),
                CancellationToken.None));
        Assert.NotNull(
            await store.TryLeaseNextAsync(
                now.AddMinutes(2),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));

        await using var context = await CreateContextAsync(host);
        var destination = await context.NotificationDestinationStates.SingleAsync();
        var outbox = await context.NotificationOutbox
            .Include(item => item.DeliveryAttempts)
            .OrderBy(item => item.NotificationVersion)
            .ToListAsync();
        Assert.True(destination.IsEnabled);
        Assert.Equal(3, outbox.Count);
        Assert.All(
            outbox.Where(item => item.NotificationVersion is 1 or 2),
            item =>
            {
                Assert.Equal(OutboxStatus.PermanentFailure, item.Status);
                Assert.Equal("{}", item.PayloadJson);
            });
        Assert.Contains(
            outbox
                .Where(item => item.NotificationVersion is 1 or 2)
                .SelectMany(item => item.DeliveryAttempts),
            attempt => attempt.Outcome == "SuppressedDestinationDisabled"
                && attempt.ErrorCode == "TelegramHttp403");
    }

    private static NotificationIntent CreateIntent(Guid jobId) =>
        new(
            "telegram-test",
            jobId,
            1,
            new JobNotification(
                "Senior .NET Engineer",
                "Example",
                ["Remote"],
                WorkplaceMode.Remote,
                90,
                5_000,
                6_000,
                "USD",
                CompensationPeriod.Month,
                DateTimeOffset.UnixEpoch,
                "https://jobs.dou.ua/vacancies/123456/"));

    private static async Task<Guid> AddJobAsync(
        PersistenceTestHost host,
        DateTimeOffset now,
        SourceName? source = null,
        string sourceJobId = "123456")
    {
        await using var context = await CreateContextAsync(host);
        var selectedSource = source ?? SourceName.Dou;
        var sourceUrl = selectedSource == SourceName.LinkedInJobSpy
            ? $"https://www.linkedin.com/jobs/view/{sourceJobId}"
            : $"https://jobs.dou.ua/vacancies/{sourceJobId}/";
        var job = JobHunter.Domain.Jobs.Job.Create(
            selectedSource,
            sourceJobId,
            sourceUrl,
            sourceUrl,
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
            5_000,
            6_000,
            "USD",
            CompensationPeriod.Month,
            now,
            PublishedAtPrecision.DateTime,
            "sha256:content",
            "v1:fingerprint",
            1,
            now);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        return job.Id;
    }

    private static Task<JobHunterDbContext> CreateContextAsync(PersistenceTestHost host) =>
        host.Services
            .GetRequiredService<IDbContextFactory<JobHunterDbContext>>()
            .CreateDbContextAsync();
}
