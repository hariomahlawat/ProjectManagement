using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class DropSimulatorNameFromProliferationGranular : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"ProliferationGranular\" DROP COLUMN IF EXISTS \"SimulatorName\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SimulatorName",
                table: "ProliferationGranular",
                type: "text",
                nullable: true);
        }
    }
}
