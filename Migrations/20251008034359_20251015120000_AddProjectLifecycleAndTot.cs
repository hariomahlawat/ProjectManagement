using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class _20251015120000_AddProjectLifecycleAndTot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "Remarks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotScope",
                table: "RemarkAudits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Projects",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CancelledOn",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CompletedOn",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletedYear",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacy",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleStatus",
                table: "Projects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<int>(
                name: "TotId",
                table: "ProjectPhotos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotId",
                table: "ProjectDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotId",
                table: "ProjectDocumentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCelebrationsInCalendar",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateTable(
                name: "ProjectTots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NotStarted"),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("INSERT INTO \"ProjectTots\" (\"ProjectId\", \"Status\") SELECT \"Id\", 'NotStarted' FROM \"Projects\"");

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_Scope_CreatedAtUtc",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "Scope", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompletedYear",
                table: "Projects",
                column: "CompletedYear");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsLegacy",
                table: "Projects",
                column: "IsLegacy");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LifecycleStatus",
                table: "Projects",
                column: "LifecycleStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_ProjectId_TotId",
                table: "ProjectPhotos",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_TotId",
                table: "ProjectPhotos",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_TotId",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_TotId",
                table: "ProjectDocuments",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_ProjectId_TotId",
                table: "ProjectDocumentRequests",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTots_ProjectId",
                table: "ProjectTots",
                column: "ProjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocuments_ProjectTots_TotId",
                table: "ProjectDocuments",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocumentRequests_ProjectTots_TotId",
                table: "ProjectDocumentRequests",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPhotos_ProjectTots_TotId",
                table: "ProjectPhotos",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectDocuments_ProjectTots_TotId",
                table: "ProjectDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectDocumentRequests_ProjectTots_TotId",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectPhotos_ProjectTots_TotId",
                table: "ProjectPhotos");

            migrationBuilder.DropTable(
                name: "ProjectTots");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_Scope_CreatedAtUtc",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CompletedYear",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_IsLegacy",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_LifecycleStatus",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectPhotos_ProjectId_TotId",
                table: "ProjectPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProjectPhotos_TotId",
                table: "ProjectPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocuments_ProjectId_TotId",
                table: "ProjectDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocuments_TotId",
                table: "ProjectDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocumentRequests_ProjectId_TotId",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "SnapshotScope",
                table: "RemarkAudits");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CancelledOn",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CompletedOn",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CompletedYear",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsLegacy",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TotId",
                table: "ProjectPhotos");

            migrationBuilder.DropColumn(
                name: "TotId",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "TotId",
                table: "ProjectDocumentRequests");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCelebrationsInCalendar",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
