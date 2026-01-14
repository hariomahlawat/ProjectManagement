using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261122120000_AddIndustryPartners")]
    public partial class AddIndustryPartners : Migration
    {
        // SECTION: Create industry partner tables
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndustryPartners",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DisplayName = table.Column<string>(maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(maxLength: 200, nullable: true),
                    PartnerType = table.Column<string>(maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                    RegistrationNumber = table.Column<string>(maxLength: 100, nullable: true),
                    Address = table.Column<string>(maxLength: 256, nullable: true),
                    City = table.Column<string>(maxLength: 120, nullable: true),
                    State = table.Column<string>(maxLength: 120, nullable: true),
                    Country = table.Column<string>(maxLength: 120, nullable: true),
                    Website = table.Column<string>(maxLength: 256, nullable: true),
                    Email = table.Column<string>(maxLength: 256, nullable: true),
                    Phone = table.Column<string>(maxLength: 50, nullable: true),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    UpdatedUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndustryPartnerProjectAssociations",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IndustryPartnerId = table.Column<int>(nullable: false),
                    ProjectId = table.Column<int>(nullable: false),
                    Role = table.Column<string>(maxLength: 120, nullable: false),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                    LinkedOnUtc = table.Column<DateTime>(nullable: false),
                    DeactivatedUtc = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryPartnerProjectAssociations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerProjectAssociations_IndustryPartners_IndustryPartnerId",
                        column: x => x.IndustryPartnerId,
                        principalTable: "IndustryPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndustryPartnerProjectAssociations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartners_DisplayName",
                table: "IndustryPartners",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId",
                table: "IndustryPartnerProjectAssociations",
                column: "IndustryPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_ProjectId",
                table: "IndustryPartnerProjectAssociations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_ProjectId_Role",
                table: "IndustryPartnerProjectAssociations",
                columns: new[] { "IndustryPartnerId", "ProjectId", "Role" },
                unique: true,
                filter: "\"IsActive\" = true");
        }

        // SECTION: Remove industry partner tables
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndustryPartnerProjectAssociations");

            migrationBuilder.DropTable(
                name: "IndustryPartners");
        }
    }
}
