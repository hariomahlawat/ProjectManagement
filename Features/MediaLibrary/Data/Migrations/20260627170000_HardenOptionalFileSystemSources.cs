using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260627170000_HardenOptionalFileSystemSources")]
public sealed class HardenOptionalFileSystemSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsVisibleInLibrary",
            table: "MediaLibrarySources",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsConfigurationManaged",
            table: "MediaLibrarySources",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "MediaLibrarySources",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "ScanIntervalMinutes",
            table: "MediaLibrarySources",
            type: "integer",
            nullable: false,
            defaultValue: 30);

        migrationBuilder.AddColumn<string>(
            name: "ScanLockedBy",
            table: "MediaLibrarySources",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ScanLockExpiresAtUtc",
            table: "MediaLibrarySources",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "HealthStatus",
            table: "MediaLibrarySources",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<string>(
            name: "HealthMessage",
            table: "MediaLibrarySources",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastHealthCheckedAtUtc",
            table: "MediaLibrarySources",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DisconnectedAtUtc",
            table: "MediaLibrarySources",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(@"
            UPDATE ""MediaLibrarySources""
            SET ""SourceType"" = 'FileSystem'
            WHERE ""SourceType"" = 'NetworkShare';

            UPDATE ""MediaAssets""
            SET ""Origin"" = 'ExternalFile'
            WHERE ""Origin"" = 'NetworkFile';

            UPDATE ""MediaLibrarySources""
            SET ""IsConfigurationManaged"" = TRUE
            WHERE ""SourceType"" = 'FileSystem'
              AND ""ConfigurationFingerprint"" IS NOT NULL;

            UPDATE ""MediaLibrarySources""
            SET ""HealthStatus"" = CASE
                WHEN ""SourceType"" = 'Prism' THEN 'Internal'
                WHEN ""LastSuccessfulScanAtUtc"" IS NOT NULL THEN 'Reachable'
                ELSE 'Unknown'
            END;");

        migrationBuilder.DropIndex(
            name: "IX_MediaLibrarySources_IsEnabled_SourceType",
            table: "MediaLibrarySources");

        migrationBuilder.CreateIndex(
            name: "IX_MediaLibrarySources_IsEnabled_IsDeleted_SourceType",
            table: "MediaLibrarySources",
            columns: new[] { "IsEnabled", "IsDeleted", "SourceType" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaLibrarySources_IsVisibleInLibrary_IsDeleted",
            table: "MediaLibrarySources",
            columns: new[] { "IsVisibleInLibrary", "IsDeleted" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaLibrarySources_ScanLockExpiresAtUtc",
            table: "MediaLibrarySources",
            column: "ScanLockExpiresAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_MediaLibrarySources_IsEnabled_IsDeleted_SourceType", "MediaLibrarySources");
        migrationBuilder.DropIndex("IX_MediaLibrarySources_IsVisibleInLibrary_IsDeleted", "MediaLibrarySources");
        migrationBuilder.DropIndex("IX_MediaLibrarySources_ScanLockExpiresAtUtc", "MediaLibrarySources");

        migrationBuilder.Sql(@"
            UPDATE ""MediaLibrarySources"" SET ""SourceType"" = 'NetworkShare' WHERE ""SourceType"" = 'FileSystem';
            UPDATE ""MediaAssets"" SET ""Origin"" = 'NetworkFile' WHERE ""Origin"" = 'ExternalFile';");

        migrationBuilder.DropColumn("IsVisibleInLibrary", "MediaLibrarySources");
        migrationBuilder.DropColumn("IsConfigurationManaged", "MediaLibrarySources");
        migrationBuilder.DropColumn("IsDeleted", "MediaLibrarySources");
        migrationBuilder.DropColumn("ScanIntervalMinutes", "MediaLibrarySources");
        migrationBuilder.DropColumn("ScanLockedBy", "MediaLibrarySources");
        migrationBuilder.DropColumn("ScanLockExpiresAtUtc", "MediaLibrarySources");
        migrationBuilder.DropColumn("HealthStatus", "MediaLibrarySources");
        migrationBuilder.DropColumn("HealthMessage", "MediaLibrarySources");
        migrationBuilder.DropColumn("LastHealthCheckedAtUtc", "MediaLibrarySources");
        migrationBuilder.DropColumn("DisconnectedAtUtc", "MediaLibrarySources");

        migrationBuilder.CreateIndex(
            name: "IX_MediaLibrarySources_IsEnabled_SourceType",
            table: "MediaLibrarySources",
            columns: new[] { "IsEnabled", "SourceType" });
    }
}
