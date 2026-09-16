using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiAnalysisRevisionIdentity : Migration
    {
        private static readonly string[] AnalysisIdentityColumns =
        [
            "JobId",
            "CandidateProfileSnapshotId",
            "JobRevisionNumber",
            "Provider",
            "SchemaVersion",
            "RubricVersion"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAnalyses_JobId",
                table: "AiAnalyses");

            migrationBuilder.AddColumn<int>(
                name: "JobRevisionNumber",
                table: "AiAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_JobId_CandidateProfileSnapshotId_JobRevisionNumber_Provider_SchemaVersion_RubricVersion",
                table: "AiAnalyses",
                columns: AnalysisIdentityColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAnalyses_JobId_CandidateProfileSnapshotId_JobRevisionNumber_Provider_SchemaVersion_RubricVersion",
                table: "AiAnalyses");

            migrationBuilder.DropColumn(
                name: "JobRevisionNumber",
                table: "AiAnalyses");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_JobId",
                table: "AiAnalyses",
                column: "JobId");
        }
    }
}
