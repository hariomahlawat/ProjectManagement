using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270305090000_EnsureProjectTotApprovalColumns")]
    public partial class EnsureProjectTotApprovalColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ProjectTots"
                    ADD COLUMN IF NOT EXISTS "LastApprovedByUserId" character varying(450);
                    """);

                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ProjectTots"
                    ADD COLUMN IF NOT EXISTS "LastApprovedOnUtc" timestamp without time zone;
                    """);

                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM pg_indexes
                            WHERE schemaname = current_schema()
                              AND tablename = 'projecttots'
                              AND indexname = 'ix_projecttots_lastapprovedbyuserid'
                        ) THEN
                            CREATE INDEX "IX_ProjectTots_LastApprovedByUserId" ON "ProjectTots" ("LastApprovedByUserId");
                        END IF;
                    END$$;
                    """);

                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM pg_constraint
                            WHERE conname = 'fk_projecttots_aspnetusers_lastapprovedbyuserid'
                        ) THEN
                            ALTER TABLE "ProjectTots"
                            ADD CONSTRAINT "FK_ProjectTots_AspNetUsers_LastApprovedByUserId"
                            FOREIGN KEY ("LastApprovedByUserId")
                            REFERENCES "AspNetUsers" ("Id")
                            ON DELETE RESTRICT;
                        END IF;
                    END$$;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('ProjectTots', 'LastApprovedByUserId') IS NULL
                        ALTER TABLE [ProjectTots] ADD [LastApprovedByUserId] nvarchar(450) NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('ProjectTots', 'LastApprovedOnUtc') IS NULL
                        ALTER TABLE [ProjectTots] ADD [LastApprovedOnUtc] datetime2 NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = 'IX_ProjectTots_LastApprovedByUserId'
                          AND object_id = OBJECT_ID('ProjectTots')
                    )
                        CREATE INDEX [IX_ProjectTots_LastApprovedByUserId]
                        ON [ProjectTots] ([LastApprovedByUserId]);
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.foreign_keys
                        WHERE name = 'FK_ProjectTots_AspNetUsers_LastApprovedByUserId'
                    )
                        ALTER TABLE [ProjectTots]
                        ADD CONSTRAINT [FK_ProjectTots_AspNetUsers_LastApprovedByUserId]
                        FOREIGN KEY ([LastApprovedByUserId])
                        REFERENCES [AspNetUsers]([Id])
                        ON DELETE NO ACTION;
                    """);
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "LastApprovedByUserId",
                    table: "ProjectTots",
                    maxLength: 450,
                    nullable: true);

                migrationBuilder.AddColumn<DateTime>(
                    name: "LastApprovedOnUtc",
                    table: "ProjectTots",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_ProjectTots_LastApprovedByUserId",
                    table: "ProjectTots",
                    column: "LastApprovedByUserId");

                migrationBuilder.AddForeignKey(
                    name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                    table: "ProjectTots",
                    column: "LastApprovedByUserId",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """DROP INDEX IF EXISTS "IX_ProjectTots_LastApprovedByUserId";""");

                migrationBuilder.Sql(
                    """
                    ALTER TABLE "ProjectTots"
                    DROP CONSTRAINT IF EXISTS "FK_ProjectTots_AspNetUsers_LastApprovedByUserId";
                    """);

                migrationBuilder.Sql(
                    """ALTER TABLE "ProjectTots" DROP COLUMN IF EXISTS "LastApprovedOnUtc";""");
                migrationBuilder.Sql(
                    """ALTER TABLE "ProjectTots" DROP COLUMN IF EXISTS "LastApprovedByUserId";""");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1 FROM sys.foreign_keys
                        WHERE name = 'FK_ProjectTots_AspNetUsers_LastApprovedByUserId'
                    )
                        ALTER TABLE [ProjectTots]
                        DROP CONSTRAINT [FK_ProjectTots_AspNetUsers_LastApprovedByUserId];
                    """);

                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = 'IX_ProjectTots_LastApprovedByUserId'
                          AND object_id = OBJECT_ID('ProjectTots')
                    )
                        DROP INDEX [IX_ProjectTots_LastApprovedByUserId] ON [ProjectTots];
                    """);

                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('ProjectTots', 'LastApprovedOnUtc') IS NOT NULL
                        ALTER TABLE [ProjectTots] DROP COLUMN [LastApprovedOnUtc];
                    """);

                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('ProjectTots', 'LastApprovedByUserId') IS NOT NULL
                        ALTER TABLE [ProjectTots] DROP COLUMN [LastApprovedByUserId];
                    """);
            }
            else
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                    table: "ProjectTots");

                migrationBuilder.DropIndex(
                    name: "IX_ProjectTots_LastApprovedByUserId",
                    table: "ProjectTots");

                migrationBuilder.DropColumn(
                    name: "LastApprovedOnUtc",
                    table: "ProjectTots");

                migrationBuilder.DropColumn(
                    name: "LastApprovedByUserId",
                    table: "ProjectTots");
            }
        }
    }
}
