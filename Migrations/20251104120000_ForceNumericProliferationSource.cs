using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class ForceNumericProliferationSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProlifYearly_Project_Source_Year",
                table: "ProliferationYearly");

            migrationBuilder.DropIndex(
                name: "IX_ProliferationGranular_ProjectId_Source_ProliferationDate",
                table: "ProliferationGranular");

            migrationBuilder.DropIndex(
                name: "UX_ProlifYearPref_Project_Source_Year",
                table: "ProliferationYearPreference");

            migrationBuilder.AddColumn<int>(
                name: "Source_tmp",
                table: "ProliferationYearly",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source_tmp",
                table: "ProliferationGranular",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source_tmp",
                table: "ProliferationYearPreference",
                nullable: true);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearly"
                    SET "Source_tmp" = CASE
                        WHEN regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') ~ '^[0-9]+$' THEN CAST("Source" AS INTEGER)
                        WHEN lower(btrim(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular"
                    SET "Source_tmp" = CASE
                        WHEN regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') ~ '^[0-9]+$' THEN CAST("Source" AS INTEGER)
                        WHEN lower(btrim(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference"
                    SET "Source_tmp" = CASE
                        WHEN regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') ~ '^[0-9]+$' THEN CAST("Source" AS INTEGER)
                        WHEN lower(btrim(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationYearly]
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, [Source]) IS NOT NULL THEN TRY_CONVERT(int, [Source])
                        WHEN LOWER(LTRIM(RTRIM(CAST([Source] AS nvarchar(32))))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST([Source] AS nvarchar(32))), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationGranular]
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, [Source]) IS NOT NULL THEN TRY_CONVERT(int, [Source])
                        WHEN LOWER(LTRIM(RTRIM(CAST([Source] AS nvarchar(32))))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST([Source] AS nvarchar(32))), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationYearPreference]
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, [Source]) IS NOT NULL THEN TRY_CONVERT(int, [Source])
                        WHEN LOWER(LTRIM(RTRIM(CAST([Source] AS nvarchar(32))))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST([Source] AS nvarchar(32))), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearly"
                    SET "Source_tmp" = CASE
                        WHEN CAST("Source" AS TEXT) GLOB '[0-9][0-9]*' THEN CAST("Source" AS INTEGER)
                        WHEN LOWER(TRIM(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST("Source" AS TEXT)), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular"
                    SET "Source_tmp" = CASE
                        WHEN CAST("Source" AS TEXT) GLOB '[0-9][0-9]*' THEN CAST("Source" AS INTEGER)
                        WHEN LOWER(TRIM(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST("Source" AS TEXT)), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference"
                    SET "Source_tmp" = CASE
                        WHEN CAST("Source" AS TEXT) GLOB '[0-9][0-9]*' THEN CAST("Source" AS INTEGER)
                        WHEN LOWER(TRIM(CAST("Source" AS TEXT))) = 'sdd' THEN 1
                        WHEN REPLACE(REPLACE(LOWER(CAST("Source" AS TEXT)), '-', ''), ' ', '') IN ('abw515', '515abw') THEN 2
                        ELSE NULL
                    END;
                    """);
            }

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationYearly");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationGranular");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationYearPreference");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationYearly",
                newName: "Source");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationGranular",
                newName: "Source");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationYearPreference",
                newName: "Source");

            migrationBuilder.AlterColumn<int>(
                name: "Source",
                table: "ProliferationYearly",
                nullable: false,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Source",
                table: "ProliferationGranular",
                nullable: false,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Source",
                table: "ProliferationYearPreference",
                nullable: false,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProlifYearly_Project_Source_Year",
                table: "ProliferationYearly",
                columns: new[] { "ProjectId", "Source", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationGranular_ProjectId_Source_ProliferationDate",
                table: "ProliferationGranular",
                columns: new[] { "ProjectId", "Source", "ProliferationDate" });

            migrationBuilder.CreateIndex(
                name: "UX_ProlifYearPref_Project_Source_Year",
                table: "ProliferationYearPreference",
                columns: new[] { "ProjectId", "Source", "Year" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProlifYearly_Project_Source_Year",
                table: "ProliferationYearly");

            migrationBuilder.DropIndex(
                name: "IX_ProliferationGranular_ProjectId_Source_ProliferationDate",
                table: "ProliferationGranular");

            migrationBuilder.DropIndex(
                name: "UX_ProlifYearPref_Project_Source_Year",
                table: "ProliferationYearPreference");

            migrationBuilder.AddColumn<string>(
                name: "Source_tmp",
                table: "ProliferationYearly",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source_tmp",
                table: "ProliferationGranular",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source_tmp",
                table: "ProliferationYearPreference",
                nullable: true);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearly"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationYearly]
                    SET [Source_tmp] = CASE
                        WHEN [Source] = 1 THEN 'Sdd'
                        WHEN [Source] = 2 THEN 'Abw515'
                        ELSE CAST([Source] AS nvarchar(32))
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationGranular]
                    SET [Source_tmp] = CASE
                        WHEN [Source] = 1 THEN 'Sdd'
                        WHEN [Source] = 2 THEN 'Abw515'
                        ELSE CAST([Source] AS nvarchar(32))
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE [ProliferationYearPreference]
                    SET [Source_tmp] = CASE
                        WHEN [Source] = 1 THEN 'Sdd'
                        WHEN [Source] = 2 THEN 'Abw515'
                        ELSE CAST([Source] AS nvarchar(32))
                    END;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearly"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference"
                    SET "Source_tmp" = CASE
                        WHEN "Source" = 1 THEN 'Sdd'
                        WHEN "Source" = 2 THEN 'Abw515'
                        ELSE CAST("Source" AS TEXT)
                    END;
                    """);
            }

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationYearly");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationGranular");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProliferationYearPreference");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationYearly",
                newName: "Source");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationGranular",
                newName: "Source");

            migrationBuilder.RenameColumn(
                name: "Source_tmp",
                table: "ProliferationYearPreference",
                newName: "Source");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "ProliferationYearly",
                nullable: false,
                oldClrType: typeof(int));

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "ProliferationGranular",
                nullable: false,
                oldClrType: typeof(int));

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "ProliferationYearPreference",
                nullable: false,
                oldClrType: typeof(int));

            migrationBuilder.CreateIndex(
                name: "IX_ProlifYearly_Project_Source_Year",
                table: "ProliferationYearly",
                columns: new[] { "ProjectId", "Source", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationGranular_ProjectId_Source_ProliferationDate",
                table: "ProliferationGranular",
                columns: new[] { "ProjectId", "Source", "ProliferationDate" });

            migrationBuilder.CreateIndex(
                name: "UX_ProlifYearPref_Project_Source_Year",
                table: "ProliferationYearPreference",
                columns: new[] { "ProjectId", "Source", "Year" },
                unique: true);
        }
    }
}
