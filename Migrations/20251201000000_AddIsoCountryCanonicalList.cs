using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Services.Startup;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddIsoCountryCanonicalList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "FfcCountries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.Sql("UPDATE \"FfcCountries\" SET \"Name\" = TRIM(\"Name\");");
            migrationBuilder.Sql("UPDATE \"FfcCountries\" SET \"IsoCode\" = UPPER(TRIM(\"IsoCode\")) WHERE \"IsoCode\" IS NOT NULL;");

            try
            {
                foreach (var row in IsoCountrySeedData.Load())
                {
                    var name = row.Name?.Trim();
                    var iso = row.Alpha3?.Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(iso))
                    {
                        continue;
                    }

                    var escapedName = name.Replace("'", "''", StringComparison.Ordinal);
                    migrationBuilder.Sql($@"
UPDATE \"FfcCountries\"
SET \"IsoCode\" = '{iso}', \"Name\" = '{escapedName}'
WHERE LOWER(\"Name\") = LOWER('{escapedName}');
");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to apply ISO-3166 canonical data during migration.", ex);
            }

            migrationBuilder.Sql(@"
WITH missing AS (
    SELECT \"Id\", ROW_NUMBER() OVER (ORDER BY \"Id\") AS seq
    FROM \"FfcCountries\"
    WHERE \"IsoCode\" IS NULL OR LENGTH(TRIM(\"IsoCode\")) = 0
)
UPDATE \"FfcCountries\"
SET \"IsoCode\" = CONCAT('X', RIGHT(CONCAT('00', UPPER(TO_HEX(seq - 1))), 2))
FROM missing
WHERE \"FfcCountries\".\"Id\" = missing.\"Id\";
");

            migrationBuilder.Sql("UPDATE \"FfcCountries\" SET \"IsoCode\" = UPPER(TRIM(\"IsoCode\"));");

            migrationBuilder.AlterColumn<string>(
                name: "IsoCode",
                table: "FfcCountries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_FfcCountries_IsoCode",
                table: "FfcCountries",
                column: "IsoCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_FfcCountries_IsoCode",
                table: "FfcCountries");

            migrationBuilder.AlterColumn<string>(
                name: "IsoCode",
                table: "FfcCountries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "FfcCountries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
