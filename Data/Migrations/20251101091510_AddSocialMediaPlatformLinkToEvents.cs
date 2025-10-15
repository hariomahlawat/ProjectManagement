using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ProjectManagement.Data.ApplicationDbContext))]
    [Migration("20251101091510_AddSocialMediaPlatformLinkToEvents")]
    public partial class AddSocialMediaPlatformLinkToEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.Sql("""
                    INSERT INTO "SocialMediaPlatforms" ("Id", "Name", "Description", "IsActive", "CreatedAtUtc", "CreatedByUserId", "RowVersion")
                    SELECT (
                            substring(md5('Unspecified') from 1 for 8) || '-' ||
                            substring(md5('Unspecified') from 9 for 4) || '-' ||
                            substring(md5('Unspecified') from 13 for 4) || '-' ||
                            substring(md5('Unspecified') from 17 for 4) || '-' ||
                            substring(md5('Unspecified') from 21 for 12)
                        )::uuid,
                        'Unspecified',
                        NULL,
                        TRUE,
                        NOW() AT TIME ZONE 'utc',
                        'system-migration',
                        decode(md5(random()::text), 'hex')
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SocialMediaPlatforms" existing WHERE existing."Name" = 'Unspecified'
                    );
                """);

                migrationBuilder.Sql("""
                    INSERT INTO "SocialMediaPlatforms" ("Id", "Name", "Description", "IsActive", "CreatedAtUtc", "CreatedByUserId", "RowVersion")
                    SELECT platform_id, platform_name, NULL, TRUE, NOW() AT TIME ZONE 'utc', 'system-migration', decode(md5(random()::text), 'hex')
                    FROM (
                        SELECT DISTINCT
                            CASE
                                WHEN platform_trimmed IS NULL OR platform_trimmed = '' THEN 'Unspecified'
                                ELSE platform_trimmed
                            END AS platform_name,
                            (
                                substring(md5(COALESCE(platform_trimmed, 'Unspecified')) from 1 for 8) || '-' ||
                                substring(md5(COALESCE(platform_trimmed, 'Unspecified')) from 9 for 4) || '-' ||
                                substring(md5(COALESCE(platform_trimmed, 'Unspecified')) from 13 for 4) || '-' ||
                                substring(md5(COALESCE(platform_trimmed, 'Unspecified')) from 17 for 4) || '-' ||
                                substring(md5(COALESCE(platform_trimmed, 'Unspecified')) from 21 for 12)
                            )::uuid AS platform_id
                        FROM (
                            SELECT TRIM("Platform") AS platform_trimmed
                            FROM "SocialMediaEvents"
                        ) AS platforms
                    ) AS distinct_platforms
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SocialMediaPlatforms" existing WHERE existing."Name" = distinct_platforms.platform_name
                    );
                """);

                migrationBuilder.Sql("""
                    UPDATE "SocialMediaEvents" e
                    SET "SocialMediaPlatformId" = p."Id"
                    FROM "SocialMediaPlatforms" p
                    WHERE p."Name" = COALESCE(NULLIF(TRIM(e."Platform"), ''), 'Unspecified')
                        AND (e."SocialMediaPlatformId" IS NULL OR e."SocialMediaPlatformId" <> p."Id");
                """);

                migrationBuilder.Sql("""
                    UPDATE "SocialMediaEvents"
                    SET "SocialMediaPlatformId" = p."Id"
                    FROM "SocialMediaPlatforms" p
                    WHERE p."Name" = 'Unspecified'
                        AND "SocialMediaPlatformId" IS NULL;
                """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId");

                migrationBuilder.AddForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId",
                    principalTable: "SocialMediaPlatforms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                migrationBuilder.DropColumn(
                    name: "Platform",
                    table: "SocialMediaEvents");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "uniqueidentifier",
                    nullable: true);

                migrationBuilder.Sql("""
                    IF NOT EXISTS (SELECT 1 FROM [SocialMediaPlatforms] WHERE [Name] = 'Unspecified')
                    BEGIN
                        INSERT INTO [SocialMediaPlatforms] ([Id], [Name], [Description], [IsActive], [CreatedAtUtc], [CreatedByUserId], [RowVersion])
                        VALUES (NEWID(), 'Unspecified', NULL, 1, SYSUTCDATETIME(), 'system-migration', HASHBYTES('MD5', CAST(NEWID() AS nvarchar(36))));
                    END;
                """);

                migrationBuilder.Sql("""
                    WITH DistinctPlatforms AS (
                        SELECT DISTINCT LTRIM(RTRIM([Platform])) AS [Name]
                        FROM [SocialMediaEvents]
                    )
                    INSERT INTO [SocialMediaPlatforms] ([Id], [Name], [Description], [IsActive], [CreatedAtUtc], [CreatedByUserId], [RowVersion])
                    SELECT NEWID(), COALESCE(NULLIF(dp.[Name], ''), 'Unspecified'), NULL, 1, SYSUTCDATETIME(), 'system-migration', HASHBYTES('MD5', CAST(NEWID() AS nvarchar(36))))
                    FROM DistinctPlatforms dp
                    WHERE NOT EXISTS (SELECT 1 FROM [SocialMediaPlatforms] existing WHERE existing.[Name] = COALESCE(NULLIF(dp.[Name], ''), 'Unspecified'));
                """);

                migrationBuilder.Sql("""
                    UPDATE e
                    SET [SocialMediaPlatformId] = p.[Id]
                    FROM [SocialMediaEvents] e
                    INNER JOIN [SocialMediaPlatforms] p ON p.[Name] = COALESCE(NULLIF(LTRIM(RTRIM(e.[Platform])), ''), 'Unspecified');
                """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "uniqueidentifier",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uniqueidentifier",
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId");

                migrationBuilder.AddForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId",
                    principalTable: "SocialMediaPlatforms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                migrationBuilder.DropColumn(
                    name: "Platform",
                    table: "SocialMediaEvents");
            }
            else
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "TEXT",
                    nullable: true);

                migrationBuilder.Sql("""
                    INSERT INTO "SocialMediaPlatforms" ("Id", "Name", "Description", "IsActive", "CreatedAtUtc", "CreatedByUserId", "RowVersion")
                    SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))), 'Unspecified', NULL, 1, CURRENT_TIMESTAMP, 'system-migration', randomblob(16)
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SocialMediaPlatforms" existing WHERE existing."Name" = 'Unspecified'
                    );
                """);

                migrationBuilder.Sql("""
                    INSERT INTO "SocialMediaPlatforms" ("Id", "Name", "Description", "IsActive", "CreatedAtUtc", "CreatedByUserId", "RowVersion")
                    SELECT
                        lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
                        platform_name,
                        NULL,
                        1,
                        CURRENT_TIMESTAMP,
                        'system-migration',
                        randomblob(16)
                    FROM (
                        SELECT DISTINCT COALESCE(NULLIF(TRIM("Platform"), ''), 'Unspecified') AS platform_name
                        FROM "SocialMediaEvents"
                    ) AS platforms
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SocialMediaPlatforms" existing WHERE existing."Name" = platforms.platform_name
                    );
                """);

                migrationBuilder.Sql("""
                    UPDATE "SocialMediaEvents"
                    SET "SocialMediaPlatformId" = (
                        SELECT "Id"
                        FROM "SocialMediaPlatforms"
                        WHERE "Name" = COALESCE(NULLIF(TRIM("Platform"), ''), 'Unspecified')
                        LIMIT 1
                    );
                """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    type: "TEXT",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "TEXT",
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId");

                migrationBuilder.AddForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaPlatformId",
                    principalTable: "SocialMediaPlatforms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                migrationBuilder.DropColumn(
                    name: "Platform",
                    table: "SocialMediaEvents");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddColumn<string>(
                    name: "Platform",
                    table: "SocialMediaEvents",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true);

                migrationBuilder.Sql("""
                    UPDATE "SocialMediaEvents" e
                    SET "Platform" = p."Name"
                    FROM "SocialMediaPlatforms" p
                    WHERE p."Id" = e."SocialMediaPlatformId";
                """);

                migrationBuilder.DropForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropColumn(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddColumn<string>(
                    name: "Platform",
                    table: "SocialMediaEvents",
                    type: "nvarchar(128)",
                    maxLength: 128,
                    nullable: true);

                migrationBuilder.Sql("""
                    UPDATE e
                    SET [Platform] = p.[Name]
                    FROM [SocialMediaEvents] e
                    INNER JOIN [SocialMediaPlatforms] p ON p.[Id] = e.[SocialMediaPlatformId];
                """);

                migrationBuilder.DropForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropColumn(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "Platform",
                    table: "SocialMediaEvents",
                    type: "TEXT",
                    nullable: true);

                migrationBuilder.Sql("""
                    UPDATE "SocialMediaEvents"
                    SET "Platform" = (
                        SELECT "Name" FROM "SocialMediaPlatforms" WHERE "Id" = "SocialMediaPlatformId" LIMIT 1
                    );
                """);

                migrationBuilder.DropForeignKey(
                    name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropIndex(
                    name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                    table: "SocialMediaEvents");

                migrationBuilder.DropColumn(
                    name: "SocialMediaPlatformId",
                    table: "SocialMediaEvents");
            }
        }
    }
}
