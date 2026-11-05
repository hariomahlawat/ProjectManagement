using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Five fixed, optional key points per project (training gap, op necessity, training realism,
    /// ease of learning, proliferation potential) for briefing deck project slides.
    /// Additive: a new table with no rows; nothing reads it until a deck shows key points.
    /// </remarks>
    public partial class AddProjectKeyPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectKeyPoints",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<short>(type: "smallint", nullable: false),
                    Text = table.Column<string>(type: "character varying(130)", maxLength: 130, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectKeyPoints", x => new { x.ProjectId, x.Kind });
                    table.CheckConstraint("CK_ProjectKeyPoints_Kind_Positive", "\"Kind\" >= 1");
                    table.ForeignKey(
                        name: "FK_ProjectKeyPoints_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectKeyPoints");
        }
    }
}
