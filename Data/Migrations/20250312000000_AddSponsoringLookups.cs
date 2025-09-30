using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    public partial class AddSponsoringLookups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";
            var isNpgsql = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var timestampDefault = isSqlServer
                ? "GETUTCDATE()"
                : isNpgsql
                    ? "now() at time zone 'utc'"
                    : "CURRENT_TIMESTAMP";

            if (isSqlServer)
            {
                migrationBuilder.CreateTable(
                    name: "LineDirectorates",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_LineDirectorates", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SponsoringUnits",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SponsoringUnits", x => x.Id);
                    });
            }
            else if (isNpgsql)
            {
                migrationBuilder.CreateTable(
                    name: "LineDirectorates",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_LineDirectorates", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SponsoringUnits",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SponsoringUnits", x => x.Id);
                    });
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "LineDirectorates",
                    columns: table => new
                    {
                        Id = table.Column<int>(nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        Name = table.Column<string>(maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_LineDirectorates", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SponsoringUnits",
                    columns: table => new
                    {
                        Id = table.Column<int>(nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        Name = table.Column<string>(maxLength: 200, nullable: false),
                        IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                        SortOrder = table.Column<int>(nullable: false, defaultValue: 0),
                        CreatedUtc = table.Column<DateTime>(nullable: false, defaultValueSql: timestampDefault),
                        UpdatedUtc = table.Column<DateTime>(nullable: false, defaultValueSql: timestampDefault)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SponsoringUnits", x => x.Id);
                    });
            }

            migrationBuilder.CreateIndex(
                name: "IX_LineDirectorates_Name",
                table: "LineDirectorates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SponsoringUnits_Name",
                table: "SponsoringUnits",
                column: "Name",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "SponsoringLineDirectorateId",
                table: "Projects",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SponsoringUnitId",
                table: "Projects",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalSponsoringLineDirectorateId",
                table: "ProjectMetaChangeRequests",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalSponsoringUnitId",
                table: "ProjectMetaChangeRequests",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SponsoringLineDirectorateId",
                table: "Projects",
                column: "SponsoringLineDirectorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SponsoringUnitId",
                table: "Projects",
                column: "SponsoringUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_LineDirectorates_SponsoringLineDirectorateId",
                table: "Projects",
                column: "SponsoringLineDirectorateId",
                principalTable: "LineDirectorates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_SponsoringUnits_SponsoringUnitId",
                table: "Projects",
                column: "SponsoringUnitId",
                principalTable: "SponsoringUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_LineDirectorates_SponsoringLineDirectorateId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_SponsoringUnits_SponsoringUnitId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_SponsoringLineDirectorateId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_SponsoringUnitId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SponsoringLineDirectorateId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SponsoringUnitId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OriginalSponsoringLineDirectorateId",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropColumn(
                name: "OriginalSponsoringUnitId",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropTable(
                name: "LineDirectorates");

            migrationBuilder.DropTable(
                name: "SponsoringUnits");
        }
    }
}
