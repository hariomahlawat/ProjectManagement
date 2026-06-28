using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628103000_AddMediaAvailabilityState")]
public sealed class AddMediaAvailabilityState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AvailabilityStatus",
            table: "MediaAssets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Available");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastAvailabilityCheckUtc",
            table: "MediaAssets",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UnavailableReason",
            table: "MediaAssets",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UnavailableSinceUtc",
            table: "MediaAssets",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "MediaAssets"
            SET "AvailabilityStatus" = CASE WHEN "IsAvailable" THEN 'Available' ELSE 'SourceMissing' END,
                "UnavailableReason" = CASE WHEN "IsAvailable" THEN NULL ELSE "ProcessingFailureReason" END,
                "UnavailableSinceUtc" = CASE WHEN "IsAvailable" THEN NULL ELSE "LastSeenAtUtc" END;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_MediaAssets_AvailabilityStatus_IsDeleted_MediaDateUtc",
            table: "MediaAssets",
            columns: new[] { "AvailabilityStatus", "IsDeleted", "MediaDateUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MediaAssets_AvailabilityStatus_IsDeleted_MediaDateUtc",
            table: "MediaAssets");
        migrationBuilder.DropColumn(name: "AvailabilityStatus", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "LastAvailabilityCheckUtc", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "UnavailableReason", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "UnavailableSinceUtc", table: "MediaAssets");
    }
}
