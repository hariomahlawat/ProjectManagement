using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT project attachments (docs/ux/it-projects.md, PR 5): one table of file records keyed to
    /// ItProjects. Additive: a new table nothing else depends on, so safe during an overlapping
    /// deployment and unused after a rollback. Hand-written because the model snapshot lags the
    /// model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261229080016_AddItProjectAttachments")]
    public partial class AddItProjectAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItProjectAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItProjectId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectAttachments_ItProjects_ItProjectId",
                        column: x => x.ItProjectId,
                        principalTable: "ItProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectAttachments_Project_Deleted_Uploaded",
                table: "ItProjectAttachments",
                columns: new[] { "ItProjectId", "IsDeleted", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItProjectAttachments");
        }
    }
}
