using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125220000_AddProjectIdeas")]
    public partial class AddProjectIdeas : Migration
    {
        // SECTION: Create lightweight Project Ideas tables
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectIdeas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedProjectOfficerUserId = table.Column<string>(type: "text", nullable: true),
                    AssignedHodUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ArchiveReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectIdeas", x => x.Id);
                    table.ForeignKey("FK_ProjectIdeas_AspNetUsers_AssignedHodUserId", x => x.AssignedHodUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_ProjectIdeas_AspNetUsers_AssignedProjectOfficerUserId", x => x.AssignedProjectOfficerUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_ProjectIdeas_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectIdeaComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectIdeaId = table.Column<int>(type: "integer", nullable: false),
                    CommentText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                }, constraints: table => { table.PrimaryKey("PK_ProjectIdeaComments", x => x.Id); table.ForeignKey("FK_ProjectIdeaComments_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_ProjectIdeaComments_ProjectIdeas_ProjectIdeaId", x => x.ProjectIdeaId, "ProjectIdeas", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable(
                name: "ProjectIdeaDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectIdeaId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                }, constraints: table => { table.PrimaryKey("PK_ProjectIdeaDocuments", x => x.Id); table.ForeignKey("FK_ProjectIdeaDocuments_AspNetUsers_UploadedByUserId", x => x.UploadedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_ProjectIdeaDocuments_ProjectIdeas_ProjectIdeaId", x => x.ProjectIdeaId, "ProjectIdeas", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable(
                name: "ProjectIdeaNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectIdeaId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                }, constraints: table => { table.PrimaryKey("PK_ProjectIdeaNotes", x => x.Id); table.ForeignKey("FK_ProjectIdeaNotes_AspNetUsers_CreatedByUserId", x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_ProjectIdeaNotes_ProjectIdeas_ProjectIdeaId", x => x.ProjectIdeaId, "ProjectIdeas", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateIndex("IX_ProjectIdeas_AssignedHodUserId", "ProjectIdeas", "AssignedHodUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_AssignedProjectOfficerUserId", "ProjectIdeas", "AssignedProjectOfficerUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_CreatedAt", "ProjectIdeas", "CreatedAt");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_CreatedByUserId", "ProjectIdeas", "CreatedByUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_IsDeleted", "ProjectIdeas", "IsDeleted");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_Status", "ProjectIdeas", "Status");
            migrationBuilder.CreateIndex("IX_ProjectIdeas_UpdatedAt", "ProjectIdeas", "UpdatedAt");
            migrationBuilder.CreateIndex("IX_ProjectIdeaComments_CreatedByUserId", "ProjectIdeaComments", "CreatedByUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeaComments_IsDeleted", "ProjectIdeaComments", "IsDeleted");
            migrationBuilder.CreateIndex("IX_ProjectIdeaComments_ProjectIdeaId", "ProjectIdeaComments", "ProjectIdeaId");
            migrationBuilder.CreateIndex("IX_ProjectIdeaDocuments_IsDeleted", "ProjectIdeaDocuments", "IsDeleted");
            migrationBuilder.CreateIndex("IX_ProjectIdeaDocuments_ProjectIdeaId", "ProjectIdeaDocuments", "ProjectIdeaId");
            migrationBuilder.CreateIndex("IX_ProjectIdeaDocuments_UploadedByUserId", "ProjectIdeaDocuments", "UploadedByUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeaNotes_CreatedByUserId", "ProjectIdeaNotes", "CreatedByUserId");
            migrationBuilder.CreateIndex("IX_ProjectIdeaNotes_IsDeleted", "ProjectIdeaNotes", "IsDeleted");
            migrationBuilder.CreateIndex("IX_ProjectIdeaNotes_ProjectIdeaId", "ProjectIdeaNotes", "ProjectIdeaId");
        }

        // SECTION: Remove Project Ideas tables
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("ProjectIdeaComments");
            migrationBuilder.DropTable("ProjectIdeaDocuments");
            migrationBuilder.DropTable("ProjectIdeaNotes");
            migrationBuilder.DropTable("ProjectIdeas");
        }
    }
}
