using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyRuleEvaluationScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "RuleEvaluations");

            migrationBuilder.DropColumn(
                name: "MissingDataJson",
                table: "RuleEvaluations");

            migrationBuilder.DropColumn(
                name: "RulesAndAiThreshold",
                table: "RuleEvaluations");

            migrationBuilder.DropColumn(
                name: "RulesOnlyThreshold",
                table: "RuleEvaluations");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "RuleEvaluations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "RuleEvaluations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MissingDataJson",
                table: "RuleEvaluations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RulesAndAiThreshold",
                table: "RuleEvaluations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RulesOnlyThreshold",
                table: "RuleEvaluations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "RuleEvaluations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
