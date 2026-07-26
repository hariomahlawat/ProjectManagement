using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207110000_AddArppVerificationState")]
public partial class AddArppVerificationState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsVerified",
            table: "ArppIssues",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "VerifiedAtUtc",
            table: "ArppIssues",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerifiedByUserId",
            table: "ArppIssues",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerificationNote",
            table: "ArppIssues",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppIssues_VerificationState",
            table: "ArppIssues",
            sql: "(NOT \"IsVerified\" AND \"VerifiedAtUtc\" IS NULL AND \"VerifiedByUserId\" IS NULL AND \"VerificationNote\" IS NULL) OR " +
                 "(\"IsVerified\" AND \"VerifiedAtUtc\" IS NOT NULL AND length(btrim(\"VerifiedByUserId\")) > 0)");

        migrationBuilder.CreateIndex(
            name: "IX_ArppIssues_IsVerified",
            table: "ArppIssues",
            column: "IsVerified");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppIssues_VerificationState",
            table: "ArppIssues");

        migrationBuilder.DropIndex(
            name: "IX_ArppIssues_IsVerified",
            table: "ArppIssues");

        migrationBuilder.DropColumn(name: "IsVerified", table: "ArppIssues");
        migrationBuilder.DropColumn(name: "VerifiedAtUtc", table: "ArppIssues");
        migrationBuilder.DropColumn(name: "VerifiedByUserId", table: "ArppIssues");
        migrationBuilder.DropColumn(name: "VerificationNote", table: "ArppIssues");
    }
}
