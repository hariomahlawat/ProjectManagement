using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201210000_AddHolidayClassificationAndObservance")]
public partial class AddHolidayClassificationAndObservance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Holidays_Date",
            table: "Holidays");

        migrationBuilder.AddColumn<string>(
            name: "AuthorityReference",
            table: "Holidays",
            type: "character varying(240)",
            maxLength: 240,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsObservedAsOfficeHoliday",
            table: "Holidays",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "ObservanceChangedByUserId",
            table: "Holidays",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ObservanceChangedUtc",
            table: "Holidays",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ObservanceRemarks",
            table: "Holidays",
            type: "character varying(1200)",
            maxLength: 1200,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Type",
            table: "Holidays",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        // Existing records preserve their current behaviour: every previous holiday remains
        // a Gazetted office holiday and therefore continues to affect working-day calculations.
        migrationBuilder.Sql(
            """
            UPDATE "Holidays"
            SET "Type" = 1,
                "IsObservedAsOfficeHoliday" = TRUE
            WHERE "Type" IS DISTINCT FROM 1
               OR "IsObservedAsOfficeHoliday" IS DISTINCT FROM TRUE;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Holidays_Type",
            table: "Holidays",
            sql: "\"Type\" IN (1, 2)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Holidays_GazettedObserved",
            table: "Holidays",
            sql: "\"Type\" <> 1 OR \"IsObservedAsOfficeHoliday\" = TRUE");

        migrationBuilder.CreateIndex(
            name: "IX_Holidays_Date",
            table: "Holidays",
            column: "Date");

        migrationBuilder.CreateIndex(
            name: "IX_Holidays_Date_Type",
            table: "Holidays",
            columns: new[] { "Date", "Type" });

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX "UX_Holidays_OneGazettedPerDate"
            ON "Holidays" ("Date")
            WHERE "Type" = 1;

            CREATE UNIQUE INDEX "UX_Holidays_DateTypeNormalisedName"
            ON "Holidays" ("Date", "Type", lower(btrim("Name")));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "UX_Holidays_DateTypeNormalisedName";
            DROP INDEX IF EXISTS "UX_Holidays_OneGazettedPerDate";
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Holidays_GazettedObserved",
            table: "Holidays");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Holidays_Type",
            table: "Holidays");

        migrationBuilder.DropIndex(
            name: "IX_Holidays_Date_Type",
            table: "Holidays");

        migrationBuilder.DropIndex(
            name: "IX_Holidays_Date",
            table: "Holidays");

        migrationBuilder.DropColumn(name: "AuthorityReference", table: "Holidays");
        migrationBuilder.DropColumn(name: "IsObservedAsOfficeHoliday", table: "Holidays");
        migrationBuilder.DropColumn(name: "ObservanceChangedByUserId", table: "Holidays");
        migrationBuilder.DropColumn(name: "ObservanceChangedUtc", table: "Holidays");
        migrationBuilder.DropColumn(name: "ObservanceRemarks", table: "Holidays");
        migrationBuilder.DropColumn(name: "Type", table: "Holidays");

        // This recreates the previous schema only when each date still has at most one entry.
        // It intentionally fails rather than silently discarding classified holiday records.
        migrationBuilder.CreateIndex(
            name: "IX_Holidays_Date",
            table: "Holidays",
            column: "Date",
            unique: true);
    }
}
