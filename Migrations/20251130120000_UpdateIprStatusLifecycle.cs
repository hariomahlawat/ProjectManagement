using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIprStatusLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"IprRecords\" SET \"Status\" = 'FilingUnderProcess' WHERE \"Status\" = 'Draft';");
            migrationBuilder.Sql("UPDATE \"IprRecords\" SET \"Status\" = 'Withdrawn' WHERE \"Status\" = 'Expired';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"IprRecords\" SET \"Status\" = 'Draft' WHERE \"Status\" = 'FilingUnderProcess';");
            migrationBuilder.Sql("UPDATE \"IprRecords\" SET \"Status\" = 'Expired' WHERE \"Status\" = 'Withdrawn';");
        }
    }
}
