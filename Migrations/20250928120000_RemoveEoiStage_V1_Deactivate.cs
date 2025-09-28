using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEoiStage_V1_Deactivate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

            if (isSqlServer)
            {
                migrationBuilder.Sql(@"DELETE FROM StageChangeLogs WHERE StageCode = 'EOI';
DELETE FROM StageChangeRequests WHERE StageCode = 'EOI';
DELETE FROM ProjectStages WHERE StageCode = 'EOI';
DELETE FROM StagePlans WHERE StageCode = 'EOI';");
            }
            else
            {
                migrationBuilder.Sql(@"DELETE FROM \"StageChangeLogs\" WHERE \"StageCode\" = 'EOI';
DELETE FROM \"StageChangeRequests\" WHERE \"StageCode\" = 'EOI';
DELETE FROM \"ProjectStages\" WHERE \"StageCode\" = 'EOI';
DELETE FROM \"StagePlans\" WHERE \"StageCode\" = 'EOI';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
