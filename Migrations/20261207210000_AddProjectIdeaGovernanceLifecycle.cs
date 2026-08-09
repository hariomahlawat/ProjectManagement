using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261207210000_AddProjectIdeaGovernanceLifecycle")]
    public partial class AddProjectIdeaGovernanceLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ProjectIdeas",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "ProjectIdeas",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeleteReason",
                table: "ProjectIdeas",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProjectIdeas",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "ProjectIdeaComments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditedByUserId",
                table: "ProjectIdeaComments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ProjectIdeaComments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "ProjectIdeaComments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProjectIdeaComments",
                type: "bytea",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ProjectIdeas"
                SET "RowVersion" = decode(
                    md5(random()::text || clock_timestamp()::text || "Id"::text),
                    'hex')
                WHERE "RowVersion" IS NULL;

                UPDATE "ProjectIdeaComments"
                SET "RowVersion" = decode(
                    md5(random()::text || clock_timestamp()::text || "Id"::text),
                    'hex')
                WHERE "RowVersion" IS NULL;
                """);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "ProjectIdeas",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "ProjectIdeaComments",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectIdeas_Deleted_DeletedAt",
                table: "ProjectIdeas",
                columns: new[] { "IsDeleted", "DeletedAt" },
                descending: new[] { false, true });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectIdeas_Deleted_DeletedAt",
                table: "ProjectIdeas");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ProjectIdeas");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "ProjectIdeas");

            migrationBuilder.DropColumn(
                name: "DeleteReason",
                table: "ProjectIdeas");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProjectIdeas");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "EditedByUserId",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "ProjectIdeaComments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProjectIdeaComments");
        }
    }
}
