using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207160000_AddArppPppNumberAndCategoryIdentifiers")]
public partial class AddArppPppNumberAndCategoryIdentifiers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppEntries_RequiredText",
            table: "ArppEntries");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppPublishedEntries_RequiredText",
            table: "ArppPublishedEntries");

        migrationBuilder.AlterColumn<string>(
            name: "SerialNumber",
            table: "ArppEntries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "SerialNumber",
            table: "ArppPublishedEntries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AddColumn<string>(
            name: "PppNumber",
            table: "ArppEntries",
            type: "character varying(160)",
            maxLength: 160,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PppNumber",
            table: "ArppPublishedEntries",
            type: "character varying(160)",
            maxLength: 160,
            nullable: true);

        // Delisted rows in the issued ARPP do not carry Serial No. or PPP No.
        // Existing values are cleared rather than preserving identifiers that were
        // entered only because the earlier schema required them.
        migrationBuilder.Sql("""
            UPDATE "ArppEntries"
            SET "SerialNumber" = NULL,
                "PppNumber" = NULL
            WHERE "Category" = 4;

            UPDATE "ArppPublishedEntries"
            SET "SerialNumber" = NULL,
                "PppNumber" = NULL
            WHERE "Category" = 4;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppEntries_RequiredText",
            table: "ArppEntries",
            sql: "length(btrim(\"ProjectReference\")) > 0 AND " +
                 "length(btrim(\"Cfa\")) > 0 AND " +
                 "length(btrim(\"Fund\")) > 0 AND " +
                 "length(btrim(\"DfpdsSchedule\")) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppEntries_IssuedIdentifiers",
            table: "ArppEntries",
            sql: "(\"Category\" = 4 AND \"SerialNumber\" IS NULL AND \"PppNumber\" IS NULL) OR " +
                 "(\"Category\" IN (1, 2, 3) AND \"SerialNumber\" IS NOT NULL AND length(btrim(\"SerialNumber\")) > 0 AND " +
                 "(\"PppNumber\" IS NULL OR length(btrim(\"PppNumber\")) > 0))");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppPublishedEntries_RequiredText",
            table: "ArppPublishedEntries",
            sql: "length(btrim(\"ProjectReference\")) > 0 AND " +
                 "length(btrim(\"Cfa\")) > 0 AND " +
                 "length(btrim(\"Fund\")) > 0 AND " +
                 "length(btrim(\"DfpdsSchedule\")) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppPublishedEntries_IssuedIdentifiers",
            table: "ArppPublishedEntries",
            sql: "(\"Category\" = 4 AND \"SerialNumber\" IS NULL AND \"PppNumber\" IS NULL) OR " +
                 "(\"Category\" IN (1, 2, 3) AND \"SerialNumber\" IS NOT NULL AND length(btrim(\"SerialNumber\")) > 0 AND " +
                 "(\"PppNumber\" IS NULL OR length(btrim(\"PppNumber\")) > 0))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppEntries_IssuedIdentifiers",
            table: "ArppEntries");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppPublishedEntries_IssuedIdentifiers",
            table: "ArppPublishedEntries");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppEntries_RequiredText",
            table: "ArppEntries");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ArppPublishedEntries_RequiredText",
            table: "ArppPublishedEntries");

        migrationBuilder.Sql("""
            UPDATE "ArppEntries"
            SET "SerialNumber" = COALESCE(NULLIF(btrim("SerialNumber"), ''), "SortOrder"::text);

            UPDATE "ArppPublishedEntries"
            SET "SerialNumber" = COALESCE(NULLIF(btrim("SerialNumber"), ''), "SortOrder"::text);
            """);

        migrationBuilder.DropColumn(
            name: "PppNumber",
            table: "ArppEntries");

        migrationBuilder.DropColumn(
            name: "PppNumber",
            table: "ArppPublishedEntries");

        migrationBuilder.AlterColumn<string>(
            name: "SerialNumber",
            table: "ArppEntries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "SerialNumber",
            table: "ArppPublishedEntries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppEntries_RequiredText",
            table: "ArppEntries",
            sql: "length(btrim(\"SerialNumber\")) > 0 AND " +
                 "length(btrim(\"ProjectReference\")) > 0 AND " +
                 "length(btrim(\"Cfa\")) > 0 AND " +
                 "length(btrim(\"Fund\")) > 0 AND " +
                 "length(btrim(\"DfpdsSchedule\")) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ArppPublishedEntries_RequiredText",
            table: "ArppPublishedEntries",
            sql: "length(btrim(\"SerialNumber\")) > 0 AND " +
                 "length(btrim(\"ProjectReference\")) > 0 AND " +
                 "length(btrim(\"Cfa\")) > 0 AND " +
                 "length(btrim(\"Fund\")) > 0 AND " +
                 "length(btrim(\"DfpdsSchedule\")) > 0");
    }
}
