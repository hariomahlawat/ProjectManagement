using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddProcessStages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Row = table.Column<int>(type: "integer", nullable: true),
                    Col = table.Column<int>(type: "integer", nullable: true),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessChecklistItems_ProcessStages_StageId",
                        column: x => x.StageId,
                        principalTable: "ProcessStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessStageEdges",
                columns: table => new
                {
                    FromStageId = table.Column<int>(type: "integer", nullable: false),
                    ToStageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStageEdges", x => new { x.FromStageId, x.ToStageId });
                    table.ForeignKey(
                        name: "FK_ProcessStageEdges_ProcessStages_FromStageId",
                        column: x => x.FromStageId,
                        principalTable: "ProcessStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessStageEdges_ProcessStages_ToStageId",
                        column: x => x.ToStageId,
                        principalTable: "ProcessStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessChecklistItems_StageId",
                table: "ProcessChecklistItems",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStageEdges_ToStageId",
                table: "ProcessStageEdges",
                column: "ToStageId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessChecklistItems");

            migrationBuilder.DropTable(
                name: "ProcessStageEdges");

            migrationBuilder.DropTable(
                name: "ProcessStages");
        }
    }
}
