using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class RenameProliferationYearlySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ProliferationYearlies'
          AND column_name = 'RegisterationSourceId'
    ) THEN
        IF EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'ProliferationYearlies'
              AND column_name = 'Source'
        ) THEN
            UPDATE "ProliferationYearlies"
            SET "Source" = COALESCE("Source", "RegisterationSourceId"::text);
            ALTER TABLE "ProliferationYearlies" DROP COLUMN "RegisterationSourceId";
        ELSE
            ALTER TABLE "ProliferationYearlies" RENAME COLUMN "RegisterationSourceId" TO "Source";
        END IF;
    END IF;
END $$;
""");

                migrationBuilder.Sql("""
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ProliferationYearlies'
          AND column_name = 'Source'
    ) THEN
        ALTER TABLE "ProliferationYearlies"
            ALTER COLUMN "Source" TYPE character varying(64)
            USING "Source"::text;
        ALTER TABLE "ProliferationYearlies"
            ALTER COLUMN "Source" SET NOT NULL;
    END IF;
END $$;
""");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'RegisterationSourceId' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Source' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
    BEGIN
        UPDATE [dbo].[ProliferationYearlies]
        SET [Source] = COALESCE([Source], CAST([RegisterationSourceId] AS nvarchar(64)))
        WHERE [RegisterationSourceId] IS NOT NULL;

        ALTER TABLE [dbo].[ProliferationYearlies] DROP COLUMN [RegisterationSourceId];
    END
    ELSE
    BEGIN
        EXEC sp_rename N'[dbo].[ProliferationYearlies].[RegisterationSourceId]', N'Source', 'COLUMN';
    END
END;

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Source' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
BEGIN
    ALTER TABLE [dbo].[ProliferationYearlies] ALTER COLUMN [Source] nvarchar(64) NOT NULL;
END;
""");
            }
            else
            {
                // Providers such as SQLite already have the correct schema when running the full migration chain.
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ProliferationYearlies'
          AND column_name = 'Source'
    ) THEN
        IF EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'ProliferationYearlies'
              AND column_name = 'RegisterationSourceId'
        ) THEN
            UPDATE "ProliferationYearlies"
            SET "RegisterationSourceId" = COALESCE("RegisterationSourceId", "Source"::text);
            ALTER TABLE "ProliferationYearlies" DROP COLUMN "Source";
        ELSE
            ALTER TABLE "ProliferationYearlies" RENAME COLUMN "Source" TO "RegisterationSourceId";
        END IF;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ProliferationYearlies'
          AND column_name = 'RegisterationSourceId'
    ) THEN
        ALTER TABLE "ProliferationYearlies"
            ALTER COLUMN "RegisterationSourceId" TYPE character varying(64)
            USING "RegisterationSourceId"::text;
        ALTER TABLE "ProliferationYearlies"
            ALTER COLUMN "RegisterationSourceId" SET NOT NULL;
    END IF;
END $$;
""");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Source' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'RegisterationSourceId' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
    BEGIN
        UPDATE [dbo].[ProliferationYearlies]
        SET [RegisterationSourceId] = COALESCE([RegisterationSourceId], [Source]);
        ALTER TABLE [dbo].[ProliferationYearlies] DROP COLUMN [Source];
    END
    ELSE
    BEGIN
        EXEC sp_rename N'[dbo].[ProliferationYearlies].[Source]', N'RegisterationSourceId', 'COLUMN';
    END
END;

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'RegisterationSourceId' AND Object_ID = Object_ID(N'[dbo].[ProliferationYearlies]'))
BEGIN
    ALTER TABLE [dbo].[ProliferationYearlies] ALTER COLUMN [RegisterationSourceId] nvarchar(64) NOT NULL;
END;
""");
            }
            else
            {
                // No-op for other providers.
            }
        }
    }
}
