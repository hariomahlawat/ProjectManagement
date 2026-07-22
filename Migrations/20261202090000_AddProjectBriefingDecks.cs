using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261202090000_AddProjectBriefingDecks")]
public partial class AddProjectBriefingDecks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectBriefingDecks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                PresentationMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CostMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IncludeStageSummary = table.Column<bool>(type: "boolean", nullable: false),
                IncludeProjectCategorySummary = table.Column<bool>(type: "boolean", nullable: false),
                IncludeTechnicalCategorySummary = table.Column<bool>(type: "boolean", nullable: false),
                HandlingMarking = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                SelectionRulesJson = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastGeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectBriefingDecks", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectBriefingDecks_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectBriefingDeckItems",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeckId = table.Column<long>(type: "bigint", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                BriefDescriptionOverride = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                AddedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectBriefingDeckItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectBriefingDeckItems_ProjectBriefingDecks_DeckId",
                    column: x => x.DeckId,
                    principalTable: "ProjectBriefingDecks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProjectBriefingDeckItems_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_NormalizedName",
            table: "ProjectBriefingDecks",
            columns: new[] { "OwnerUserId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDecks_OwnerUserId_UpdatedAtUtc",
            table: "ProjectBriefingDecks",
            columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDeckItems_DeckId_ProjectId",
            table: "ProjectBriefingDeckItems",
            columns: new[] { "DeckId", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDeckItems_DeckId_SortOrder",
            table: "ProjectBriefingDeckItems",
            columns: new[] { "DeckId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectBriefingDeckItems_ProjectId",
            table: "ProjectBriefingDeckItems",
            column: "ProjectId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProjectBriefingDeckItems");
        migrationBuilder.DropTable(name: "ProjectBriefingDecks");
    }
}
