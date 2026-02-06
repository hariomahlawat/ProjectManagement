using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125120000_AddIndustryPartners")]
    public partial class AddIndustryPartners : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Industry partners tables
            migrationBuilder.CreateTable(
                name: "IndustryPartners",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(maxLength: 200, nullable: false),
                    Location = table.Column<string>(maxLength: 2000, nullable: true),
                    NormalizedLocation = table.Column<string>(maxLength: 2000, nullable: true),
                    Remarks = table.Column<string>(maxLength: 4000, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(nullable: false),
                    CreatedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(nullable: true),
                    UpdatedByUserId = table.Column<string>(maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_IndustryPartners", x => x.Id); });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    IndustryPartnerId = table.Column<int>(nullable: false),
                    OriginalFileName = table.Column<string>(maxLength: 260, nullable: false),
                    StorageKey = table.Column<string>(maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(nullable: false),
                    Sha256 = table.Column<string>(maxLength: 64, nullable: false),
                    UploadedUtc = table.Column<DateTimeOffset>(nullable: false),
                    UploadedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerAttachments_IndustryPartners_IndustryPartnerId",
                        column: x => x.IndustryPartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerContacts",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IndustryPartnerId = table.Column<int>(nullable: false),
                    Name = table.Column<string>(maxLength: 200, nullable: true),
                    Phone = table.Column<string>(maxLength: 64, nullable: true),
                    Email = table.Column<string>(maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerContacts", x => x.Id);
                    table.CheckConstraint("CK_IndustryPartnerContacts_PhoneOrEmail", "(nullif(trim(coalesce(\"Phone\", '')), '') is not null) OR (nullif(trim(coalesce(\"Email\", '')), '') is not null)");
                    table.ForeignKey(
                        name: "FK_IndustryPartnerContacts_IndustryPartners_IndustryPartnerId",
                        column: x => x.IndustryPartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerProjects",
                columns: table => new
                {
                    IndustryPartnerId = table.Column<int>(nullable: false),
                    ProjectId = table.Column<int>(nullable: false),
                    LinkedUtc = table.Column<DateTimeOffset>(nullable: false),
                    LinkedByUserId = table.Column<string>(maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerProjects", x => new { x.IndustryPartnerId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_IndustryPartnerProjects_IndustryPartners_IndustryPartnerId",
                        column: x => x.IndustryPartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerAttachments_IndustryPartnerId",
                table: "IndustryPartnerAttachments",
                column: "IndustryPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerContacts_IndustryPartnerId",
                table: "IndustryPartnerContacts",
                column: "IndustryPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjects_ProjectId",
                table: "IndustryPartnerProjects",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_IndustryPartners_NormalizedName_LocationNull",
                table: "IndustryPartners",
                column: "NormalizedName",
                unique: true,
                filter: "\"NormalizedLocation\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_IndustryPartners_NormalizedName_NormalizedLocation",
                table: "IndustryPartners",
                columns: new[] { "NormalizedName", "NormalizedLocation" },
                unique: true,
                filter: "\"NormalizedLocation\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IndustryPartnerAttachments");
            migrationBuilder.DropTable(name: "IndustryPartnerContacts");
            migrationBuilder.DropTable(name: "IndustryPartnerProjects");
            migrationBuilder.DropTable(name: "IndustryPartners");
        }
    }
}
