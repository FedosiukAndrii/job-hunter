using Microsoft.EntityFrameworkCore;
using JobHunter.Domain.AI;
using JobHunter.Domain.Common;
using JobHunter.Domain.Evaluation;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Operations;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Sources;

namespace JobHunter.Infrastructure.Persistence;

public sealed class JobHunterDbContext(DbContextOptions<JobHunterDbContext> options)
    : DbContext(options)
{
    public DbSet<CandidateProfileSnapshot> CandidateProfiles => Set<CandidateProfileSnapshot>();

    public DbSet<SourceSubscription> SourceSubscriptions => Set<SourceSubscription>();

    public DbSet<SourceRun> SourceRuns => Set<SourceRun>();

    public DbSet<SourceCursor> SourceCursors => Set<SourceCursor>();

    public DbSet<JobHunter.Domain.Jobs.Job> Jobs => Set<JobHunter.Domain.Jobs.Job>();

    public DbSet<JobRevision> JobRevisions => Set<JobRevision>();

    public DbSet<JobObservation> JobObservations => Set<JobObservation>();

    public DbSet<JobPossibleDuplicate> JobPossibleDuplicates => Set<JobPossibleDuplicate>();

    public DbSet<RuleEvaluation> RuleEvaluations => Set<RuleEvaluation>();

    public DbSet<AiAnalysis> AiAnalyses => Set<AiAnalysis>();

    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();

    public DbSet<NotificationDestinationState> NotificationDestinationStates =>
        Set<NotificationDestinationState>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementConcurrencyVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        IncrementConcurrencyVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureCandidateProfile(modelBuilder);
        ConfigureSources(modelBuilder);
        ConfigureJobs(modelBuilder);
        ConfigureEvaluations(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureApplicationEvents(modelBuilder);
    }

    private static void ConfigureCandidateProfile(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<CandidateProfileSnapshot>();
        profile.ToTable("CandidateProfiles");
        profile.HasKey(entity => entity.Id);
        profile.Property(entity => entity.ContentHash).HasMaxLength(71).IsRequired();
        profile.Property(entity => entity.CanonicalJson).IsRequired();
        profile.Property(entity => entity.SourcePath).HasMaxLength(1024).IsRequired();
        profile.HasIndex(entity => entity.ProfileVersion).IsUnique();
        profile.HasIndex(entity => entity.ContentHash).IsUnique();
    }

    private static void ConfigureSources(ModelBuilder modelBuilder)
    {
        var subscription = modelBuilder.Entity<SourceSubscription>();
        subscription.ToTable("SourceSubscriptions");
        subscription.HasKey(entity => entity.Id);
        subscription.Property(entity => entity.Source)
            .HasConversion(
                source => source.Value,
                value => SourceName.Create(value))
            .HasMaxLength(64)
            .IsRequired();
        subscription.Property(entity => entity.SubscriptionKey).HasMaxLength(256).IsRequired();
        subscription.Property(entity => entity.ConfigurationJson).IsRequired();
        subscription.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(32);
        subscription.Property(entity => entity.StatusReasonCode).HasMaxLength(128);
        subscription.Property(entity => entity.StatusDiagnostic).HasMaxLength(1024);
        subscription.Property(entity => entity.ConcurrencyVersion).IsConcurrencyToken();
        subscription.HasIndex(entity => new { entity.Source, entity.SubscriptionKey }).IsUnique();

        var run = modelBuilder.Entity<SourceRun>();
        run.ToTable("SourceRuns");
        run.HasKey(entity => entity.Id);
        run.Property(entity => entity.LeaseToken).HasMaxLength(32);
        run.Property(entity => entity.ErrorCode).HasMaxLength(128);
        run.Property(entity => entity.Diagnostic).HasMaxLength(1024);
        run.Property(entity => entity.CursorJson).HasMaxLength(8192);
        run.Property(entity => entity.ConcurrencyVersion).IsConcurrencyToken();
        run.HasOne(entity => entity.SourceSubscription)
            .WithMany(entity => entity.Runs)
            .HasForeignKey(entity => entity.SourceSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        run.HasIndex(entity => new { entity.SourceSubscriptionId, entity.StartedAtUtc });
        run.HasIndex(entity => entity.SourceSubscriptionId)
            .IsUnique()
            .HasFilter("\"Status\" = 0");

        var cursor = modelBuilder.Entity<SourceCursor>();
        cursor.ToTable("SourceCursors");
        cursor.HasKey(entity => entity.SourceSubscriptionId);
        cursor.Property(entity => entity.EntityTag).HasMaxLength(512);
        cursor.Property(entity => entity.OpaqueValue).HasMaxLength(4096);
        cursor.HasOne(entity => entity.SourceSubscription)
            .WithOne(entity => entity.Cursor)
            .HasForeignKey<SourceCursor>(entity => entity.SourceSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureJobs(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<JobHunter.Domain.Jobs.Job>();
        job.ToTable("Jobs");
        job.HasKey(entity => entity.Id);
        job.Property(entity => entity.Source)
            .HasConversion(
                source => source.Value,
                value => SourceName.Create(value))
            .HasMaxLength(64)
            .IsRequired();
        job.Property(entity => entity.SourceJobId).HasMaxLength(512);
        job.Property(entity => entity.CanonicalUrl).HasMaxLength(2048).IsRequired();
        job.Property(entity => entity.SourceUrl).HasMaxLength(2048).IsRequired();
        job.Property(entity => entity.ApplicationUrl).HasMaxLength(2048);
        job.Property(entity => entity.Title).HasMaxLength(512).IsRequired();
        job.Property(entity => entity.Company).HasMaxLength(512).IsRequired();
        job.Property(entity => entity.DescriptionHtml).IsRequired();
        job.Property(entity => entity.DescriptionText).IsRequired();
        job.Property(entity => entity.LocationsJson).IsRequired();
        job.Property(entity => entity.Seniority).HasMaxLength(128);
        job.Property(entity => entity.SkillsJson).IsRequired();
        job.Property(entity => entity.CategoriesJson).IsRequired();
        job.Property(entity => entity.CompensationCurrency).HasMaxLength(3);
        job.Property(entity => entity.CompensationMinimum).HasPrecision(18, 2);
        job.Property(entity => entity.CompensationMaximum).HasPrecision(18, 2);
        job.Property(entity => entity.ContentHash).HasMaxLength(71).IsRequired();
        job.Property(entity => entity.Fingerprint).HasMaxLength(71).IsRequired();
        job.Property(entity => entity.StatusReason).HasMaxLength(256);
        job.Property(entity => entity.ConcurrencyVersion).IsConcurrencyToken();
        job.HasIndex(entity => new { entity.Source, entity.SourceJobId })
            .IsUnique()
            .HasFilter("\"SourceJobId\" IS NOT NULL");
        job.HasIndex(entity => new { entity.Source, entity.CanonicalUrl })
            .IsUnique()
            .HasFilter("\"SourceJobId\" IS NULL");
        job.HasIndex(entity => entity.Fingerprint);

        var revision = modelBuilder.Entity<JobRevision>();
        revision.ToTable("JobRevisions");
        revision.HasKey(entity => entity.Id);
        revision.Property(entity => entity.PreviousContentHash).HasMaxLength(71).IsRequired();
        revision.Property(entity => entity.PreviousSnapshotJson).IsRequired();
        revision.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Revisions)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        revision.HasIndex(entity => new { entity.JobId, entity.RevisionNumber }).IsUnique();

        var observation = modelBuilder.Entity<JobObservation>();
        observation.ToTable("JobObservations");
        observation.HasKey(entity => entity.Id);
        observation.Property(entity => entity.SourceUrl).HasMaxLength(2048).IsRequired();
        observation.Property(entity => entity.SourceGuid).HasMaxLength(1024);
        observation.Property(entity => entity.ParserVersion).HasMaxLength(64).IsRequired();
        observation.Property(entity => entity.ContentHash).HasMaxLength(71).IsRequired();
        observation.Property(entity => entity.RawPayloadHash).HasMaxLength(71).IsRequired();
        observation.Property(entity => entity.QueryId).HasMaxLength(256);
        observation.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Observations)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        observation.HasOne(entity => entity.SourceRun)
            .WithMany(entity => entity.Observations)
            .HasForeignKey(entity => entity.SourceRunId)
            .OnDelete(DeleteBehavior.Cascade);
        observation.HasOne<SourceSubscription>()
            .WithMany()
            .HasForeignKey(entity => entity.SourceSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
        observation.HasIndex(
                entity => new { entity.SourceRunId, entity.JobId, entity.RawPayloadHash })
            .IsUnique();

        var possibleDuplicate = modelBuilder.Entity<JobPossibleDuplicate>();
        possibleDuplicate.ToTable("JobPossibleDuplicates");
        possibleDuplicate.HasKey(entity => entity.Id);
        possibleDuplicate.Property(entity => entity.MatchReason).HasMaxLength(128).IsRequired();
        possibleDuplicate.Property(entity => entity.Confidence).HasPrecision(5, 4);
        possibleDuplicate.Property(entity => entity.Fingerprint).HasMaxLength(71).IsRequired();
        possibleDuplicate.HasOne(entity => entity.FirstJob)
            .WithMany()
            .HasForeignKey(entity => entity.FirstJobId)
            .OnDelete(DeleteBehavior.Restrict);
        possibleDuplicate.HasOne(entity => entity.SecondJob)
            .WithMany()
            .HasForeignKey(entity => entity.SecondJobId)
            .OnDelete(DeleteBehavior.Restrict);
        possibleDuplicate.HasIndex(entity => new { entity.FirstJobId, entity.SecondJobId }).IsUnique();
    }

    private static void ConfigureEvaluations(ModelBuilder modelBuilder)
    {
        var evaluation = modelBuilder.Entity<RuleEvaluation>();
        evaluation.ToTable("RuleEvaluations");
        evaluation.HasKey(entity => entity.Id);
        evaluation.Property(entity => entity.RubricVersion).HasMaxLength(64).IsRequired();
        evaluation.Property(entity => entity.RuleResultsJson).IsRequired();
        evaluation.Property(entity => entity.Explanation).HasMaxLength(2048).IsRequired();
        evaluation.HasOne(entity => entity.Job)
            .WithMany()
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        evaluation.HasOne<CandidateProfileSnapshot>()
            .WithMany()
            .HasForeignKey(entity => entity.CandidateProfileSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        evaluation.HasIndex(
                entity => new
                {
                    entity.JobId,
                    entity.CandidateProfileSnapshotId,
                    entity.JobRevisionNumber,
                    entity.RubricVersion
                })
            .IsUnique();

        var analysis = modelBuilder.Entity<AiAnalysis>();
        analysis.ToTable("AiAnalyses");
        analysis.HasKey(entity => entity.Id);
        analysis.Property(entity => entity.JobRevisionNumber).IsRequired();
        analysis.Property(entity => entity.Provider).HasMaxLength(64).IsRequired();
        analysis.Property(entity => entity.Model).HasMaxLength(128);
        analysis.Property(entity => entity.Status).HasMaxLength(64).IsRequired();
        analysis.Property(entity => entity.SchemaVersion).HasMaxLength(64).IsRequired();
        analysis.Property(entity => entity.RubricVersion).HasMaxLength(64).IsRequired();
        analysis.Property(entity => entity.WarningCode).HasMaxLength(128);
        analysis.HasOne<JobHunter.Domain.Jobs.Job>()
            .WithMany()
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        analysis.HasOne<CandidateProfileSnapshot>()
            .WithMany()
            .HasForeignKey(entity => entity.CandidateProfileSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        analysis.HasIndex(
                entity => new
                {
                    entity.JobId,
                    entity.CandidateProfileSnapshotId,
                    entity.JobRevisionNumber,
                    entity.Provider,
                    entity.SchemaVersion,
                    entity.RubricVersion
                })
            .IsUnique();
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        var destination = modelBuilder.Entity<NotificationDestinationState>();
        destination.ToTable("NotificationDestinationStates");
        destination.HasKey(entity => entity.DestinationId);
        destination.Property(entity => entity.DestinationId).HasMaxLength(256);
        destination.Property(entity => entity.FailureCode).HasMaxLength(128);
        destination.Property(entity => entity.ConcurrencyVersion).IsConcurrencyToken();

        var outbox = modelBuilder.Entity<NotificationOutbox>();
        outbox.ToTable("NotificationOutbox");
        outbox.HasKey(entity => entity.Id);
        outbox.Property(entity => entity.DestinationId).HasMaxLength(256).IsRequired();
        outbox.Property(entity => entity.LeaseToken).HasMaxLength(32);
        outbox.Property(entity => entity.PayloadJson).IsRequired();
        outbox.Property(entity => entity.ConcurrencyVersion).IsConcurrencyToken();
        outbox.HasOne<JobHunter.Domain.Jobs.Job>()
            .WithMany()
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        outbox.HasIndex(
                entity => new
                {
                    entity.DestinationId,
                    entity.JobId,
                    entity.NotificationVersion
                })
            .IsUnique();

        var attempt = modelBuilder.Entity<DeliveryAttempt>();
        attempt.ToTable("DeliveryAttempts");
        attempt.HasKey(entity => entity.Id);
        attempt.Property(entity => entity.Outcome).HasMaxLength(64).IsRequired();
        attempt.Property(entity => entity.ErrorCode).HasMaxLength(128);
        attempt.Property(entity => entity.ExternalMessageId).HasMaxLength(256);
        attempt.HasOne(entity => entity.NotificationOutbox)
            .WithMany(entity => entity.DeliveryAttempts)
            .HasForeignKey(entity => entity.NotificationOutboxId)
            .OnDelete(DeleteBehavior.Cascade);
        attempt.HasIndex(entity => new { entity.NotificationOutboxId, entity.AttemptNumber })
            .IsUnique();
    }

    private static void ConfigureApplicationEvents(ModelBuilder modelBuilder)
    {
        var applicationEvent = modelBuilder.Entity<ApplicationEvent>();
        applicationEvent.ToTable("ApplicationEvents");
        applicationEvent.HasKey(entity => entity.Id);
        applicationEvent.Property(entity => entity.EventType).HasMaxLength(128).IsRequired();
        applicationEvent.Property(entity => entity.Severity).HasMaxLength(32).IsRequired();
        applicationEvent.Property(entity => entity.Message).HasMaxLength(1024).IsRequired();
        applicationEvent.HasIndex(entity => entity.CreatedAtUtc);
    }

    private void IncrementConcurrencyVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IConcurrencyTracked>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            var property = entry.Property(entity => entity.ConcurrencyVersion);
            if (property.CurrentValue == property.OriginalValue)
            {
                property.CurrentValue = checked(property.OriginalValue + 1);
            }
        }
    }
}
