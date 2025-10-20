using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class DropSimulatorNameFromProliferationEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"ProliferationGranular\" DROP COLUMN IF EXISTS \"SimulatorName\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"ProliferationGranularEntries\" DROP COLUMN IF EXISTS \"SimulatorName\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"ProliferationGranular\" ADD COLUMN IF NOT EXISTS \"SimulatorName\" text NOT NULL DEFAULT '';");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"ProliferationGranularEntries\" ADD COLUMN IF NOT EXISTS \"SimulatorName\" text NOT NULL DEFAULT '';");
        }
    }
}
