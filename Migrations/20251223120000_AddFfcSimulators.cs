using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddFfcSimulators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FfcCountries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsoCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcCountries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FfcRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    IpaYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IpaDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IpaRemarks = table.Column<string>(type: "text", nullable: true),
                    GslYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GslDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GslRemarks = table.Column<string>(type: "text", nullable: true),
                    DeliveryYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveryRemarks = table.Column<string>(type: "text", nullable: true),
                    InstallationYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    InstallationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InstallationRemarks = table.Column<string>(type: "text", nullable: true),
                    OverallRemarks = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FfcRecords_FfcCountries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "FfcCountries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.CheckConstraint("CK_FfcRecords_IpaDateRequiresFlag", "\"IpaDate\" IS NULL OR \"IpaYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_GslDateRequiresFlag", "\"GslDate\" IS NULL OR \"GslYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_DeliveryDateRequiresFlag", "\"DeliveryDate\" IS NULL OR \"DeliveryYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_InstallationDateRequiresFlag", "\"InstallationDate\" IS NULL OR \"InstallationYes\" = TRUE");
                });

            migrationBuilder.CreateTable(
                name: "FfcProjects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FfcRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    LinkedProjectId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FfcProjects_FfcRecords_FfcRecordId",
                        column: x => x.FfcRecordId,
                        principalTable: "FfcRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FfcProjects_Projects_LinkedProjectId",
                        column: x => x.LinkedProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FfcAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FfcRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Caption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FfcAttachments_FfcRecords_FfcRecordId",
                        column: x => x.FfcRecordId,
                        principalTable: "FfcRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_FfcAttachments_SizeBytes", "\"SizeBytes\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "UX_FfcCountries_Name",
                table: "FfcCountries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FfcRecords_CountryId_Year",
                table: "FfcRecords",
                columns: new[] { "CountryId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_FfcRecords_StatusFlags",
                table: "FfcRecords",
                columns: new[] { "IpaYes", "GslYes", "DeliveryYes", "InstallationYes" });

            migrationBuilder.CreateIndex(
                name: "IX_FfcProjects_FfcRecordId",
                table: "FfcProjects",
                column: "FfcRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcProjects_LinkedProjectId",
                table: "FfcProjects",
                column: "LinkedProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcAttachments_FfcRecordId",
                table: "FfcAttachments",
                column: "FfcRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcAttachments_Kind",
                table: "FfcAttachments",
                column: "Kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FfcAttachments");

            migrationBuilder.DropTable(
                name: "FfcProjects");

            migrationBuilder.DropTable(
                name: "FfcRecords");

            migrationBuilder.DropTable(
                name: "FfcCountries");
        }
    }
}
