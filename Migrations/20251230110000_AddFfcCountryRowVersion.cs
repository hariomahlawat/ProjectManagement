using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddFfcCountryRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "FfcCountries",
                type: "bytea",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"FfcCountries\" SET \"RowVersion\" = decode(md5(random()::text || clock_timestamp()::text), 'hex') WHERE \"RowVersion\" IS NULL OR octet_length(\"RowVersion\") = 0;");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "FfcCountries",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "FfcCountries");
        }
    }
}
