using System.Globalization;
using JobHunter.Application.Persistence;
using JobHunter.Application.Sources;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Tests.Persistence;

public sealed class EfSourceRunStoreTests
{
    [Fact]
    public async Task ExpiredLeaseIsRecoveredAndAReplacementRunCanStart()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var start = DateTimeOffset.Parse(
            "2026-09-15T10:00:00Z",
            CultureInfo.InvariantCulture);
        var subscriptionId = await AddSubscriptionAsync(host, start);
        var store = host.Services.GetRequiredService<ISourceRunStore>();

        var abandoned = await store.TryStartAsync(
            subscriptionId,
            start,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        var recoveredCount = await store.RecoverExpiredAsync(
            start.AddMinutes(2),
            CancellationToken.None);
        var replacement = await store.TryStartAsync(
            subscriptionId,
            start.AddMinutes(2),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        Assert.NotNull(abandoned);
        Assert.Equal(1, recoveredCount);
        Assert.NotNull(replacement);
        Assert.NotEqual(abandoned.SourceRunId, replacement.SourceRunId);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var abandonedRun = await context.SourceRuns.SingleAsync(run => run.Id == abandoned.SourceRunId);
        Assert.Equal(SourceRunStatus.Failed, abandonedRun.Status);
        Assert.Equal("AbandonedLease", abandonedRun.ErrorCode);
    }

    [Fact]
    public async Task ExpiredLeaseRecoveryPreservesConfigurationDisabledState()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var start = DateTimeOffset.Parse(
            "2026-09-15T10:00:00Z",
            CultureInfo.InvariantCulture);
        var subscriptionId = await AddSubscriptionAsync(host, start);
        var runStore = host.Services.GetRequiredService<ISourceRunStore>();
        var subscriptionStore =
            host.Services.GetRequiredService<ISourceSubscriptionStore>();
        var abandoned = await runStore.TryStartAsync(
            subscriptionId,
            start,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(abandoned);

        await subscriptionStore.SynchronizeAsync(
            [
                new JobSourceSubscriptionDefinition(
                    SourceName.Dou,
                    "dotnet",
                    new Uri("https://jobs.dou.ua/vacancies/feeds/?category=.NET"),
                    ".NET",
                    10,
                    TimeSpan.FromMinutes(12),
                    enabled: false)
            ],
            start.AddSeconds(30),
            CancellationToken.None);
        var recoveredCount = await runStore.RecoverExpiredAsync(
            start.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(1, recoveredCount);
        var contextFactory =
            host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var subscription = await context.SourceSubscriptions.FindAsync(
            subscriptionId);
        var abandonedRun = await context.SourceRuns.FindAsync(
            abandoned.SourceRunId);
        Assert.False(subscription?.IsEnabled);
        Assert.Equal(SourceSubscriptionStatus.Disabled, subscription?.Status);
        Assert.Equal(SourceRunStatus.Failed, abandonedRun?.Status);
        Assert.Equal("AbandonedLease", abandonedRun?.ErrorCode);
    }

    [Fact]
    public async Task CompleteBlockedRunPersistsOnlyThatSubscriptionAsBlocked()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.Parse(
            "2026-09-15T10:00:00Z",
            CultureInfo.InvariantCulture);
        var jobSpySubscriptionId = await AddSubscriptionAsync(
            host,
            now,
            SourceName.LinkedInJobSpy,
            "linkedin");
        var douSubscriptionId = await AddSubscriptionAsync(
            host,
            now,
            SourceName.Dou,
            "dotnet");
        var store = host.Services.GetRequiredService<ISourceRunStore>();
        var lease = await store.TryStartAsync(
            jobSpySubscriptionId,
            now,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.NotNull(lease);

        await store.CompleteAsync(
            lease.SourceRunId,
            lease.LeaseToken,
            Application.Sources.JobSourceResult.Blocked(
                "linkedin_rate_limited",
                "LinkedIn rate limited the sidecar.",
                now.AddHours(24)),
            new JobIngestionResult(0, 0, 0, 0),
            now.AddSeconds(1),
            TimeSpan.FromHours(1),
            CancellationToken.None);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var jobSpy = await context.SourceSubscriptions.FindAsync(jobSpySubscriptionId);
        var dou = await context.SourceSubscriptions.FindAsync(douSubscriptionId);

        Assert.Equal(SourceSubscriptionStatus.Blocked, jobSpy?.Status);
        Assert.Equal(SourceSubscriptionStatus.Enabled, dou?.Status);
    }

    [Fact]
    public async Task CompletePartialRunSchedulesNextRunAtRetryAfter()
    {
        await using var host = await PersistenceTestHost.CreateAsync();
        var now = DateTimeOffset.Parse(
            "2026-09-15T10:00:00Z",
            CultureInfo.InvariantCulture);
        var retryAfterUtc = now.AddHours(4);
        var subscriptionId = await AddSubscriptionAsync(
            host,
            now,
            SourceName.LinkedInJobSpy,
            "linkedin");
        var store = host.Services.GetRequiredService<ISourceRunStore>();
        var lease = await store.TryStartAsync(
            subscriptionId,
            now,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.NotNull(lease);

        await store.CompleteAsync(
            lease.SourceRunId,
            lease.LeaseToken,
            new JobSourceResult(
                SourceRunStatus.Partial,
                [],
                null,
                false,
                now.AddHours(1),
                retryAfterUtc,
                "linkedin_partial",
                "LinkedIn returned a partial result."),
            new JobIngestionResult(0, 0, 0, 0),
            now.AddSeconds(1),
            TimeSpan.FromHours(1),
            CancellationToken.None);

        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var subscription = await context.SourceSubscriptions.FindAsync(subscriptionId);
        var run = await context.SourceRuns.FindAsync(lease.SourceRunId);

        Assert.Equal(SourceSubscriptionStatus.Enabled, subscription?.Status);
        Assert.Equal(retryAfterUtc, subscription?.NextDueAtUtc);
        Assert.Equal(retryAfterUtc, run?.RetryAfterUtc);
    }

    private static async Task<Guid> AddSubscriptionAsync(
        PersistenceTestHost host,
        DateTimeOffset now,
        SourceName? source = null,
        string key = "dotnet")
    {
        var contextFactory = host.Services.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var subscription = SourceSubscription.Create(
            source ?? SourceName.Dou,
            key,
            "{}",
            TimeSpan.FromMinutes(12),
            enabled: true,
            now);
        context.SourceSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        return subscription.Id;
    }
}
