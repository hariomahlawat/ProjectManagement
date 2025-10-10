using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectVideoTot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVideos_ProjectTots_TotId",
                table: "ProjectVideos");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVideos_ProjectId_TotId",
                table: "ProjectVideos");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVideos_TotId",
                table: "ProjectVideos");

            migrationBuilder.DropColumn(
                name: "TotId",
                table: "ProjectVideos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddColumn<int>(
                    name: "TotId",
                    table: "ProjectVideos",
                    type: "int",
                    nullable: true);
            }
            else if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddColumn<int>(
                    name: "TotId",
                    table: "ProjectVideos",
                    type: "integer",
                    nullable: true);
            }
            else
            {
                migrationBuilder.AddColumn<int>(
                    name: "TotId",
                    table: "ProjectVideos",
                    nullable: true);
            }

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_ProjectId_TotId",
                table: "ProjectVideos",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_TotId",
                table: "ProjectVideos",
                column: "TotId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVideos_ProjectTots_TotId",
                table: "ProjectVideos",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
