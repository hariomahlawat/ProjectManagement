using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207120000_AddArppControlledReferenceData")]
public partial class AddArppControlledReferenceData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArppCfaOptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppCfaOptions", x => x.Id);
                table.CheckConstraint("CK_ArppCfaOptions_Name", "length(btrim(\"Name\")) > 0");
                table.CheckConstraint("CK_ArppCfaOptions_SortOrder", "\"SortOrder\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "ArppFundOptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppFundOptions", x => x.Id);
                table.CheckConstraint("CK_ArppFundOptions_Name", "length(btrim(\"Name\")) > 0");
                table.CheckConstraint("CK_ArppFundOptions_SortOrder", "\"SortOrder\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "ArppDfpdsSchedules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                NormalizedCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppDfpdsSchedules", x => x.Id);
                table.CheckConstraint("CK_ArppDfpdsSchedules_Code", "length(btrim(\"Code\")) > 0");
                table.CheckConstraint("CK_ArppDfpdsSchedules_SortOrder", "\"SortOrder\" >= 0");
            });

        migrationBuilder.AddColumn<int>(name: "CfaOptionId", table: "ArppEntries", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "FundOptionId", table: "ArppEntries", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "DfpdsScheduleId", table: "ArppEntries", type: "integer", nullable: true);

        migrationBuilder.Sql(BackfillSql("ArppCfaOptions", "Name", "NormalizedName", "Cfa"));
        migrationBuilder.Sql(BackfillSql("ArppFundOptions", "Name", "NormalizedName", "Fund"));
        migrationBuilder.Sql(BackfillSql("ArppDfpdsSchedules", "Code", "NormalizedCode", "DfpdsSchedule"));

        migrationBuilder.Sql("""
            UPDATE "ArppEntries" AS e
            SET "CfaOptionId" = o."Id"
            FROM "ArppCfaOptions" AS o
            WHERE o."NormalizedName" = upper(regexp_replace(btrim(e."Cfa"), '\s+', ' ', 'g'));

            UPDATE "ArppEntries" AS e
            SET "FundOptionId" = o."Id"
            FROM "ArppFundOptions" AS o
            WHERE o."NormalizedName" = upper(regexp_replace(btrim(e."Fund"), '\s+', ' ', 'g'));

            UPDATE "ArppEntries" AS e
            SET "DfpdsScheduleId" = o."Id"
            FROM "ArppDfpdsSchedules" AS o
            WHERE o."NormalizedCode" = upper(regexp_replace(btrim(e."DfpdsSchedule"), '\s+', ' ', 'g'));
            """);

        migrationBuilder.CreateIndex(name: "IX_ArppCfaOptions_NormalizedName", table: "ArppCfaOptions", column: "NormalizedName", unique: true);
        migrationBuilder.CreateIndex(name: "IX_ArppCfaOptions_IsActive_SortOrder_Name", table: "ArppCfaOptions", columns: new[] { "IsActive", "SortOrder", "Name" });
        migrationBuilder.CreateIndex(name: "IX_ArppFundOptions_NormalizedName", table: "ArppFundOptions", column: "NormalizedName", unique: true);
        migrationBuilder.CreateIndex(name: "IX_ArppFundOptions_IsActive_SortOrder_Name", table: "ArppFundOptions", columns: new[] { "IsActive", "SortOrder", "Name" });
        migrationBuilder.CreateIndex(name: "IX_ArppDfpdsSchedules_NormalizedCode", table: "ArppDfpdsSchedules", column: "NormalizedCode", unique: true);
        migrationBuilder.CreateIndex(name: "IX_ArppDfpdsSchedules_IsActive_SortOrder_Code", table: "ArppDfpdsSchedules", columns: new[] { "IsActive", "SortOrder", "Code" });
        migrationBuilder.CreateIndex(name: "IX_ArppEntries_CfaOptionId", table: "ArppEntries", column: "CfaOptionId");
        migrationBuilder.CreateIndex(name: "IX_ArppEntries_FundOptionId", table: "ArppEntries", column: "FundOptionId");
        migrationBuilder.CreateIndex(name: "IX_ArppEntries_DfpdsScheduleId", table: "ArppEntries", column: "DfpdsScheduleId");

        migrationBuilder.AddForeignKey(
            name: "FK_ArppEntries_ArppCfaOptions_CfaOptionId",
            table: "ArppEntries", column: "CfaOptionId",
            principalTable: "ArppCfaOptions", principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(
            name: "FK_ArppEntries_ArppFundOptions_FundOptionId",
            table: "ArppEntries", column: "FundOptionId",
            principalTable: "ArppFundOptions", principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(
            name: "FK_ArppEntries_ArppDfpdsSchedules_DfpdsScheduleId",
            table: "ArppEntries", column: "DfpdsScheduleId",
            principalTable: "ArppDfpdsSchedules", principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_ArppEntries_ArppCfaOptions_CfaOptionId", "ArppEntries");
        migrationBuilder.DropForeignKey("FK_ArppEntries_ArppFundOptions_FundOptionId", "ArppEntries");
        migrationBuilder.DropForeignKey("FK_ArppEntries_ArppDfpdsSchedules_DfpdsScheduleId", "ArppEntries");
        migrationBuilder.DropIndex("IX_ArppEntries_CfaOptionId", "ArppEntries");
        migrationBuilder.DropIndex("IX_ArppEntries_FundOptionId", "ArppEntries");
        migrationBuilder.DropIndex("IX_ArppEntries_DfpdsScheduleId", "ArppEntries");
        migrationBuilder.DropColumn("CfaOptionId", "ArppEntries");
        migrationBuilder.DropColumn("FundOptionId", "ArppEntries");
        migrationBuilder.DropColumn("DfpdsScheduleId", "ArppEntries");
        migrationBuilder.DropTable("ArppCfaOptions");
        migrationBuilder.DropTable("ArppFundOptions");
        migrationBuilder.DropTable("ArppDfpdsSchedules");
    }

    private static string BackfillSql(string tableName, string valueColumn, string normalizedColumn, string sourceColumn)
        => $"""
            WITH source_values AS (
                SELECT DISTINCT ON (upper(regexp_replace(btrim("{sourceColumn}"), '\s+', ' ', 'g')))
                    btrim("{sourceColumn}") AS value,
                    upper(regexp_replace(btrim("{sourceColumn}"), '\s+', ' ', 'g')) AS normalized
                FROM "ArppEntries"
                WHERE length(btrim("{sourceColumn}")) > 0
                ORDER BY upper(regexp_replace(btrim("{sourceColumn}"), '\s+', ' ', 'g')), btrim("{sourceColumn}")
            ), ordered_values AS (
                SELECT value, normalized, row_number() OVER (ORDER BY value) - 1 AS sort_order
                FROM source_values
            )
            INSERT INTO "{tableName}"
                ("{valueColumn}", "{normalizedColumn}", "IsActive", "SortOrder", "CreatedAtUtc", "UpdatedAtUtc", "CreatedByUserId", "UpdatedByUserId", "RowVersion")
            SELECT value, normalized, TRUE, sort_order::integer, now(), now(), 'migration', 'migration',
                   decode(md5(random()::text || clock_timestamp()::text || value), 'hex')
            FROM ordered_values;
            """;
}
