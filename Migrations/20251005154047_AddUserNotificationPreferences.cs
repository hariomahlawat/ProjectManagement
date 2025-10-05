using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.CreateTable(
                    name: "UserNotificationPreferences",
                    columns: table => new
                    {
                        UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        Kind = table.Column<int>(type: "integer", nullable: false),
                        Allow = table.Column<bool>(type: "boolean", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_UserNotificationPreferences", x => new { x.UserId, x.Kind });
                    });

                migrationBuilder.CreateTable(
                    name: "UserProjectMutes",
                    columns: table => new
                    {
                        UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        ProjectId = table.Column<int>(type: "integer", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_UserProjectMutes", x => new { x.UserId, x.ProjectId });
                        table.ForeignKey(
                            name: "FK_UserProjectMutes_Projects_ProjectId",
                            column: x => x.ProjectId,
                            principalTable: "Projects",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "UserNotificationPreferences",
                    columns: table => new
                    {
                        UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        Kind = table.Column<int>(type: "int", nullable: false),
                        Allow = table.Column<bool>(type: "bit", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_UserNotificationPreferences", x => new { x.UserId, x.Kind });
                    });

                migrationBuilder.CreateTable(
                    name: "UserProjectMutes",
                    columns: table => new
                    {
                        UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        ProjectId = table.Column<int>(type: "int", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_UserProjectMutes", x => new { x.UserId, x.ProjectId });
                        table.ForeignKey(
                            name: "FK_UserProjectMutes_Projects_ProjectId",
                            column: x => x.ProjectId,
                            principalTable: "Projects",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectMutes_ProjectId",
                table: "UserProjectMutes",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "UserProjectMutes");
        }
    }
}
