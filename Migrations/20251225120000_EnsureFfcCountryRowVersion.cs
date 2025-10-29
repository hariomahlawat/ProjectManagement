using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class EnsureFfcCountryRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE \"FfcCountries\"
                ADD COLUMN IF NOT EXISTS \"RowVersion\" bytea NOT NULL DEFAULT '\\x';
            ");

            migrationBuilder.Sql(@"
                UPDATE \"FfcCountries\"
                SET \"RowVersion\" = decode(md5(random()::text || clock_timestamp()::text), 'hex')
                WHERE octet_length(\"RowVersion\") = 0;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE \"FfcCountries\"
                ALTER COLUMN \"RowVersion\" DROP DEFAULT;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE \"FfcCountries\"
                DROP COLUMN IF EXISTS \"RowVersion\";
            ");
        }
    }
}
