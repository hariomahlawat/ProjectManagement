using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class DocRepoHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Tags",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"Tags\" SET \"NormalizedName\" = lower(trim(\"Name\"));");
            }
            else
            {
                migrationBuilder.Sql("UPDATE Tags SET NormalizedName = LOWER(LTRIM(RTRIM(Name)));");
            }

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Tags",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: false,
                oldDefaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentDate",
                table: "Documents",
                column: "DocumentDate");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ReceivedFrom",
                table: "Documents",
                column: "ReceivedFrom");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Subject",
                table: "Documents",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagId_DocumentId",
                table: "DocumentTags",
                columns: new[] { "TagId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName",
                table: "Tags",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_DocumentDate",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ReceivedFrom",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_Subject",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentTags_TagId_DocumentId",
                table: "DocumentTags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_NormalizedName",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Tags");
        }
    }
}
