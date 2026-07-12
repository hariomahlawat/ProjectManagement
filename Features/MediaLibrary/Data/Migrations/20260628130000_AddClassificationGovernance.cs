using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628130000_AddClassificationGovernance")]
public sealed class AddClassificationGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "ClassificationIsManual", table: "MediaAssets", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "ClassificationUpdatedByUserId", table: "MediaAssets", type: "character varying(450)", maxLength: 450, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ClassifiedAtUtc", table: "MediaAssets", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ClassifierVersion", table: "MediaAssets", type: "character varying(128)", maxLength: 128, nullable: true);

        migrationBuilder.CreateTable(
            name: "MediaClassificationAudits",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                PreviousClassification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                NewClassification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PreviousWasManual = table.Column<bool>(type: "boolean", nullable: false),
                NewIsManual = table.Column<bool>(type: "boolean", nullable: false),
                ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaClassificationAudits", x => x.Id);
                table.ForeignKey("FK_MediaClassificationAudits_MediaAssets_MediaAssetId", x => x.MediaAssetId, "MediaAssets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassificationIsManual_Classification", table: "MediaAssets", columns: new[] { "ClassificationIsManual", "Classification" });
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationAudits_MediaAssetId_ChangedAtUtc", table: "MediaClassificationAudits", columns: new[] { "MediaAssetId", "ChangedAtUtc" });

        migrationBuilder.Sql("""
            UPDATE "MediaAssets"
            SET "ClassifierVersion" = COALESCE("AnalysisVersion", 'legacy'),
                "ClassifiedAtUtc" = "AnalysedAtUtc"
            WHERE "AnalysisStatus" = 'Ready';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MediaClassificationAudits");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassificationIsManual_Classification", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationIsManual", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationUpdatedByUserId", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassifiedAtUtc", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassifierVersion", table: "MediaAssets");
    }
}
