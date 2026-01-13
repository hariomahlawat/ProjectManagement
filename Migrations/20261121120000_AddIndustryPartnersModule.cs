using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261121120000_AddIndustryPartnersModule")]
    public partial class AddIndustryPartnersModule : Migration
    {
        // SECTION: Add industry partner module tables
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndustryPartners",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmName = table.Column<string>(maxLength: 256, nullable: false),
                    NormalizedFirmName = table.Column<string>(maxLength: 256, nullable: false),
                    PartnerType = table.Column<string>(maxLength: 80, nullable: true),
                    AddressText = table.Column<string>(maxLength: 512, nullable: true),
                    City = table.Column<string>(maxLength: 120, nullable: true),
                    State = table.Column<string>(maxLength: 120, nullable: true),
                    Pincode = table.Column<string>(maxLength: 20, nullable: true),
                    Website = table.Column<string>(maxLength: 256, nullable: true),
                    Notes = table.Column<string>(maxLength: 2000, nullable: true),
                    Status = table.Column<string>(maxLength: 40, nullable: false, defaultValue: "Active"),
                    CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAtUtc = table.Column<DateTime>(nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerContacts",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartnerId = table.Column<int>(nullable: false),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Designation = table.Column<string>(maxLength: 120, nullable: true),
                    Email = table.Column<string>(maxLength: 200, nullable: true),
                    IsPrimary = table.Column<bool>(nullable: false, defaultValue: false),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAtUtc = table.Column<DateTime>(nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerContacts_IndustryPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartnerId = table.Column<int>(nullable: false),
                    StorageKey = table.Column<string>(maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(nullable: false),
                    Title = table.Column<string>(maxLength: 200, nullable: true),
                    AttachmentType = table.Column<string>(maxLength: 80, nullable: true),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerAttachments_IndustryPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectIndustryPartners",
                columns: table => new
                {
                    ProjectId = table.Column<int>(nullable: false),
                    PartnerId = table.Column<int>(nullable: false),
                    Role = table.Column<string>(maxLength: 80, nullable: false, defaultValue: "Joint Development Partner"),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(maxLength: 40, nullable: false, defaultValue: "Active"),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    UpdatedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAtUtc = table.Column<DateTime>(nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectIndustryPartners", x => new { x.ProjectId, x.PartnerId });
                    table.ForeignKey(
                        name: "FK_ProjectIndustryPartners_IndustryPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectIndustryPartners_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerContactPhones",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactId = table.Column<int>(nullable: false),
                    PhoneNumber = table.Column<string>(maxLength: 40, nullable: false),
                    Label = table.Column<string>(maxLength: 32, nullable: false, defaultValue: "Mobile")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerContactPhones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerContactPhones_IndustryPartnerContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "IndustryPartnerContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerAttachments_PartnerId",
                table: "IndustryPartnerAttachments",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerAttachments_UploadedAtUtc",
                table: "IndustryPartnerAttachments",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerContacts_PartnerId",
                table: "IndustryPartnerContacts",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerContacts_PartnerId_IsPrimary",
                table: "IndustryPartnerContacts",
                columns: new[] { "PartnerId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "UX_IndustryPartnerContacts_Primary",
                table: "IndustryPartnerContacts",
                column: "PartnerId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerContactPhones_ContactId",
                table: "IndustryPartnerContactPhones",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartners_City",
                table: "IndustryPartners",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartners_FirmName",
                table: "IndustryPartners",
                column: "FirmName");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartners_Status",
                table: "IndustryPartners",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_IndustryPartners_NormalizedFirmName",
                table: "IndustryPartners",
                column: "NormalizedFirmName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectIndustryPartners_PartnerId",
                table: "ProjectIndustryPartners",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectIndustryPartners_ProjectId",
                table: "ProjectIndustryPartners",
                column: "ProjectId");
        }

        // SECTION: Remove industry partner module tables
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndustryPartnerAttachments");

            migrationBuilder.DropTable(
                name: "IndustryPartnerContactPhones");

            migrationBuilder.DropTable(
                name: "ProjectIndustryPartners");

            migrationBuilder.DropTable(
                name: "IndustryPartnerContacts");

            migrationBuilder.DropTable(
                name: "IndustryPartners");
        }
    }
}
