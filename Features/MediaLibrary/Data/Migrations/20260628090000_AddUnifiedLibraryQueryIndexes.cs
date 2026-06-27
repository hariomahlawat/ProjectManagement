using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

/// <inheritdoc />
public partial class AddUnifiedLibraryQueryIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_MediaAssets_LibraryTimeline",
            table: "MediaAssets",
            columns: new[] { "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaAssets_OriginTimeline",
            table: "MediaAssets",
            columns: new[] { "Origin", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaAssets_ProjectTimeline",
            table: "MediaAssets",
            columns: new[] { "ProjectId", "IsAvailable", "IsDeleted", "IsArchived", "MediaDateUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_MediaAssets_LibraryTimeline", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_OriginTimeline", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ProjectTimeline", table: "MediaAssets");
    }
}
