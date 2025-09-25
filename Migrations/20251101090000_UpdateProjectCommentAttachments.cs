using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProjectCommentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FileName",
                table: "ProjectCommentAttachments",
                newName: "StoredFileName");

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "ProjectCommentAttachments",
                type: "character varying(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE \"ProjectCommentAttachments\" SET \"OriginalFileName\" = \"StoredFileName\" WHERE \"OriginalFileName\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "ProjectCommentAttachments");

            migrationBuilder.RenameColumn(
                name: "StoredFileName",
                table: "ProjectCommentAttachments",
                newName: "FileName");
        }
    }
}
