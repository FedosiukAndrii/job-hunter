using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CandidateProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    CanonicalJson = table.Column<string>(type: "TEXT", nullable: false),
                    SupplementalCvRedacted = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceJobId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ApplicationUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Company = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DescriptionHtml = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionText = table.Column<string>(type: "TEXT", nullable: false),
                    LocationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    WorkplaceMode = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Seniority = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SkillsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CategoriesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CompensationMinimum = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    CompensationMaximum = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    CompensationCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    CompensationPeriod = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PublishedAtPrecision = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    FingerprintVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Lifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubscriptionKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NextDueAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BackoffUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSucceededAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StatusReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StatusDiagnostic = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateProfileSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RubricVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    UsageJson = table.Column<string>(type: "TEXT", nullable: true),
                    WarningCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAnalyses_CandidateProfiles_CandidateProfileSnapshotId",
                        column: x => x.CandidateProfileSnapshotId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiAnalyses_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobPossibleDuplicates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecondJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchReason = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPossibleDuplicates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobPossibleDuplicates_Jobs_FirstJobId",
                        column: x => x.FirstJobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobPossibleDuplicates_Jobs_SecondJobId",
                        column: x => x.SecondJobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    PreviousSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobRevisions_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateProfileSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobRevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RubricVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PassedHardFilters = table.Column<bool>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    RulesOnlyThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    RulesAndAiThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    MissingDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleEvaluations_CandidateProfiles_CandidateProfileSnapshotId",
                        column: x => x.CandidateProfileSnapshotId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RuleEvaluations_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceCursors",
                columns: table => new
                {
                    SourceSubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityTag = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OpaqueValue = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCursors", x => x.SourceSubscriptionId);
                    table.ForeignKey(
                        name: "FK_SourceCursors_SourceSubscriptions_SourceSubscriptionId",
                        column: x => x.SourceSubscriptionId,
                        principalTable: "SourceSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    HeartbeatAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ObservedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryAfterUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Diagnostic = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CursorJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceRuns_SourceSubscriptions_SourceSubscriptionId",
                        column: x => x.SourceSubscriptionId,
                        principalTable: "SourceSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationOutboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExternalMessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryAttempts_NotificationOutbox_NotificationOutboxId",
                        column: x => x.NotificationOutboxId,
                        principalTable: "NotificationOutbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SourceGuid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ParserVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    RawPayloadHash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    QueryId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobObservations_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobObservations_SourceRuns_SourceRunId",
                        column: x => x.SourceRunId,
                        principalTable: "SourceRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobObservations_SourceSubscriptions_SourceSubscriptionId",
                        column: x => x.SourceSubscriptionId,
                        principalTable: "SourceSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_CandidateProfileSnapshotId",
                table: "AiAnalyses",
                column: "CandidateProfileSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_JobId",
                table: "AiAnalyses",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_CreatedAtUtc",
                table: "ApplicationEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_ContentHash",
                table: "CandidateProfiles",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_ProfileVersion",
                table: "CandidateProfiles",
                column: "ProfileVersion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_NotificationOutboxId_AttemptNumber",
                table: "DeliveryAttempts",
                columns: new[] { "NotificationOutboxId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobObservations_JobId",
                table: "JobObservations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobObservations_SourceRunId_JobId_RawPayloadHash",
                table: "JobObservations",
                columns: new[] { "SourceRunId", "JobId", "RawPayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobObservations_SourceSubscriptionId",
                table: "JobObservations",
                column: "SourceSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPossibleDuplicates_FirstJobId_SecondJobId",
                table: "JobPossibleDuplicates",
                columns: new[] { "FirstJobId", "SecondJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPossibleDuplicates_SecondJobId",
                table: "JobPossibleDuplicates",
                column: "SecondJobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRevisions_JobId_RevisionNumber",
                table: "JobRevisions",
                columns: new[] { "JobId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Fingerprint",
                table: "Jobs",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Source_CanonicalUrl",
                table: "Jobs",
                columns: new[] { "Source", "CanonicalUrl" },
                unique: true,
                filter: "\"SourceJobId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Source_SourceJobId",
                table: "Jobs",
                columns: new[] { "Source", "SourceJobId" },
                unique: true,
                filter: "\"SourceJobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_DestinationId_JobId_NotificationVersion",
                table: "NotificationOutbox",
                columns: new[] { "DestinationId", "JobId", "NotificationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_JobId",
                table: "NotificationOutbox",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluations_CandidateProfileSnapshotId",
                table: "RuleEvaluations",
                column: "CandidateProfileSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluations_JobId_CandidateProfileSnapshotId_JobRevisionNumber_RubricVersion",
                table: "RuleEvaluations",
                columns: new[] { "JobId", "CandidateProfileSnapshotId", "JobRevisionNumber", "RubricVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceRuns_SourceSubscriptionId",
                table: "SourceRuns",
                column: "SourceSubscriptionId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRuns_SourceSubscriptionId_StartedAtUtc",
                table: "SourceRuns",
                columns: new[] { "SourceSubscriptionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceSubscriptions_Source_SubscriptionKey",
                table: "SourceSubscriptions",
                columns: new[] { "Source", "SubscriptionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAnalyses");

            migrationBuilder.DropTable(
                name: "ApplicationEvents");

            migrationBuilder.DropTable(
                name: "DeliveryAttempts");

            migrationBuilder.DropTable(
                name: "JobObservations");

            migrationBuilder.DropTable(
                name: "JobPossibleDuplicates");

            migrationBuilder.DropTable(
                name: "JobRevisions");

            migrationBuilder.DropTable(
                name: "RuleEvaluations");

            migrationBuilder.DropTable(
                name: "SourceCursors");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "SourceRuns");

            migrationBuilder.DropTable(
                name: "CandidateProfiles");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "SourceSubscriptions");
        }
    }
}
