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
                    UPDATE "ProliferationYearly" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed ~ '^[0-9]+$' THEN src.trimmed::integer
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') AS trimmed,
                               regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') AS canonical
                        FROM "ProliferationYearly"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed ~ '^[0-9]+$' THEN src.trimmed::integer
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') AS trimmed,
                               regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') AS canonical
                        FROM "ProliferationGranular"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed ~ '^[0-9]+$' THEN src.trimmed::integer
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               regexp_replace(CAST("Source" AS TEXT), '\\s+', '', 'g') AS trimmed,
                               regexp_replace(lower(CAST("Source" AS TEXT)), '[^a-z0-9]', '', 'g') AS canonical
                        FROM "ProliferationYearPreference"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE tgt
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, tgt.[Source]) IS NOT NULL THEN TRY_CONVERT(int, tgt.[Source])
                        WHEN src.Canonical LIKE 'sdd%' THEN 1
                        WHEN src.Canonical LIKE 'abw515%' OR src.Canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM [ProliferationYearly] AS tgt
                    CROSS APPLY (
                        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(CAST(tgt.[Source] AS nvarchar(64))), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS Canonical
                    ) AS src;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE tgt
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, tgt.[Source]) IS NOT NULL THEN TRY_CONVERT(int, tgt.[Source])
                        WHEN src.Canonical LIKE 'sdd%' THEN 1
                        WHEN src.Canonical LIKE 'abw515%' OR src.Canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM [ProliferationGranular] AS tgt
                    CROSS APPLY (
                        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(CAST(tgt.[Source] AS nvarchar(64))), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS Canonical
                    ) AS src;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE tgt
                    SET [Source_tmp] = CASE
                        WHEN TRY_CONVERT(int, tgt.[Source]) IS NOT NULL THEN TRY_CONVERT(int, tgt.[Source])
                        WHEN src.Canonical LIKE 'sdd%' THEN 1
                        WHEN src.Canonical LIKE 'abw515%' OR src.Canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM [ProliferationYearPreference] AS tgt
                    CROSS APPLY (
                        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(CAST(tgt.[Source] AS nvarchar(64))), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS Canonical
                    ) AS src;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearly" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed GLOB '[0-9][0-9]*' THEN CAST(src.trimmed AS INTEGER)
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               replace(replace(replace(replace(replace(replace(lower(CAST("Source" AS TEXT)), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS canonical,
                               replace(CAST("Source" AS TEXT), ' ', '') AS trimmed
                        FROM "ProliferationYearly"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationGranular" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed GLOB '[0-9][0-9]*' THEN CAST(src.trimmed AS INTEGER)
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               replace(replace(replace(replace(replace(replace(lower(CAST("Source" AS TEXT)), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS canonical,
                               replace(CAST("Source" AS TEXT), ' ', '') AS trimmed
                        FROM "ProliferationGranular"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE "ProliferationYearPreference" AS tgt
                    SET "Source_tmp" = CASE
                        WHEN src.trimmed GLOB '[0-9][0-9]*' THEN CAST(src.trimmed AS INTEGER)
                        WHEN src.canonical LIKE 'sdd%' THEN 1
                        WHEN src.canonical LIKE 'abw515%' OR src.canonical LIKE '515abw%' THEN 2
                        ELSE NULL
                    END
                    FROM (
                        SELECT "Id",
                               replace(replace(replace(replace(replace(replace(lower(CAST("Source" AS TEXT)), '-', ''), ' ', ''), '_', ''), '(', ''), ')', ''), '/', '') AS canonical,
                               replace(CAST("Source" AS TEXT), ' ', '') AS trimmed
                        FROM "ProliferationYearPreference"
                    ) AS src
                    WHERE tgt."Id" = src."Id";
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
