using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProliferationReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProliferationGranularEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Granularity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    PeriodLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DirectBeneficiaries = table.Column<int>(type: "integer", nullable: true),
                    IndirectBeneficiaries = table.Column<int>(type: "integer", nullable: true),
                    InvestmentValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationGranularEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProliferationGranularEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProliferationYearlies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    DirectBeneficiaries = table.Column<int>(type: "integer", nullable: true),
                    IndirectBeneficiaries = table.Column<int>(type: "integer", nullable: true),
                    InvestmentValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationYearlies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProliferationYearlies_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProliferationYearPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationYearPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProliferationYearPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProliferationYearPreferences_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationGranular_Project_Source_Period",
                table: "ProliferationGranularEntries",
                columns: new[] { "ProjectId", "Source", "Year", "Granularity", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationGranular_ProjectId_Year",
                table: "ProliferationGranularEntries",
                columns: new[] { "ProjectId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationYearly_ProjectId",
                table: "ProliferationYearlies",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_ProliferationYearly_Project_Source_Year",
                table: "ProliferationYearlies",
                columns: new[] { "ProjectId", "Source", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationYearPreferences_UserId",
                table: "ProliferationYearPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_ProliferationYearPreference_Project_Source_User",
                table: "ProliferationYearPreferences",
                columns: new[] { "ProjectId", "Source", "UserId" },
                unique: true);

            migrationBuilder.Sql(
                "CREATE VIEW \"vw_ProliferationGranularYearly\" AS\n" +
                "SELECT \"ProjectId\",\n" +
                "       \"Source\",\n" +
                "       \"Year\",\n" +
                "       COALESCE(SUM(\"DirectBeneficiaries\"), 0) AS \"DirectBeneficiaries\",\n" +
                "       COALESCE(SUM(\"IndirectBeneficiaries\"), 0) AS \"IndirectBeneficiaries\",\n" +
                "       COALESCE(SUM(\"InvestmentValue\"), 0::numeric) AS \"InvestmentValue\"\n" +
                "FROM \"ProliferationGranularEntries\"\n" +
                "GROUP BY \"ProjectId\", \"Source\", \"Year\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS \"vw_ProliferationGranularYearly\";");

            migrationBuilder.DropTable(
                name: "ProliferationGranularEntries");

            migrationBuilder.DropTable(
                name: "ProliferationYearlies");

            migrationBuilder.DropTable(
                name: "ProliferationYearPreferences");
        }
    }
}
