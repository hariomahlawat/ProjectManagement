using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261203090000_ShareProjectBriefingDecksAndHardenPresentation")]
public partial class ShareProjectBriefingDecksAndHardenPresentation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LastModifiedByUserId",
            table: "ProjectBriefingDecks",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "ProjectBriefingDecks"
            SET "LastModifiedByUserId" = "OwnerUserId"
            WHERE "LastModifiedByUserId" IS NULL;
            """);

        // Existing personal decks may legitimately have the same name. Resolve such
        // collisions deterministically before changing to command-workspace-wide names.
        migrationBuilder.Sql("""
            WITH ranked AS (
                SELECT
                    "Id",
                    "Name",
                    ROW_NUMBER() OVER (
                        PARTITION BY "NormalizedName"
                        ORDER BY "CreatedAtUtc", "Id") AS duplicate_number
                FROM "ProjectBriefingDecks"
            ), renamed AS (
                SELECT
                    "Id",
                    LEFT("Name", 130) || ' — Shared ' || "Id"::text AS new_name
                FROM ranked
                WHERE duplicate_number > 1
            )
            UPDATE "ProjectBriefingDecks" AS deck
            SET
                "Name" = renamed.new_name,
                "NormalizedName" = UPPER(renamed.new_name)
            FROM renamed
            WHERE deck."Id" = renamed."Id";
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_OwnerUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_NormalizedName",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_UpdatedAtUtc",
            table: "ProjectBriefingDecks");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_LastModifiedByUserId",
            table: "ProjectBriefingDecks",
            column: "LastModifiedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_UpdatedAtUtc",
            table: "ProjectBriefingDecks",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId",
            table: "ProjectBriefingDecks",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "UX_ProjectBriefingDecks_NormalizedName",
            table: "ProjectBriefingDecks",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_LastModifiedByUserId",
            table: "ProjectBriefingDecks",
            column: "LastModifiedByUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_OwnerUserId",
            table: "ProjectBriefingDecks",
            column: "OwnerUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_LastModifiedByUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_OwnerUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "IX_ProjectBriefingDecks_LastModifiedByUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "IX_ProjectBriefingDecks_UpdatedAtUtc",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropIndex(
            name: "UX_ProjectBriefingDecks_NormalizedName",
            table: "ProjectBriefingDecks");

        migrationBuilder.DropColumn(
            name: "LastModifiedByUserId",
            table: "ProjectBriefingDecks");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_NormalizedName",
            table: "ProjectBriefingDecks",
            columns: new[] { "OwnerUserId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_UpdatedAtUtc",
            table: "ProjectBriefingDecks",
            columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectBriefingDecks_AspNetUsers_OwnerUserId",
            table: "ProjectBriefingDecks",
            column: "OwnerUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
