using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    public partial class AddNotificationPreferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";
            var isNpgsql = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

            if (isSqlServer)
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
            else if (isNpgsql)
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
                        UserId = table.Column<string>(maxLength: 450, nullable: false),
                        Kind = table.Column<int>(nullable: false),
                        Allow = table.Column<bool>(nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_UserNotificationPreferences", x => new { x.UserId, x.Kind });
                    });

                migrationBuilder.CreateTable(
                    name: "UserProjectMutes",
                    columns: table => new
                    {
                        UserId = table.Column<string>(maxLength: 450, nullable: false),
                        ProjectId = table.Column<int>(nullable: false)
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "UserProjectMutes");
        }
    }
}
