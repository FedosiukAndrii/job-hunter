using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(JobHunterDbContext))]
[Migration("20260920193004_AddDuplicateNotificationSuppression")]
public partial class AddDuplicateNotificationSuppression : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "SuppressPossibleDuplicateNotifications",
            table: "NotificationOutbox",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<long>(
            name: "PublishedAtUnixTimeSeconds",
            table: "Jobs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Jobs"
            SET "PublishedAtUnixTimeSeconds" = CAST(strftime('%s', "PublishedAtUtc") AS INTEGER)
            WHERE "PublishedAtUtc" IS NOT NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_Source_PublishedAtUnixTimeSeconds",
            table: "Jobs",
            columns: ["Source", "PublishedAtUnixTimeSeconds"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Jobs_Source_PublishedAtUnixTimeSeconds",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "PublishedAtUnixTimeSeconds",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "SuppressPossibleDuplicateNotifications",
            table: "NotificationOutbox");
    }
}
