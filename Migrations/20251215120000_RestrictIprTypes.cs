using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class RestrictIprTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"IprRecords\" SET \"Type\" = 'Patent' WHERE \"Type\" IN ('Trademark','Design','TradeSecret');");
            }
            else
            {
                migrationBuilder.Sql("UPDATE [IprRecords] SET [Type] = 'Patent' WHERE [Type] IN ('Trademark','Design','TradeSecret');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Previous type information cannot be restored reliably.
        }
    }
}
