using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "TechnicalCategories",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "TechnicalCategories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ProjectCategories",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "ProjectCategories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"ProjectCategories\" SET \"CreatedByUserId\" = 'system' WHERE \"CreatedByUserId\" IS NULL;");
                migrationBuilder.Sql("UPDATE \"ProjectCategories\" SET \"CreatedAt\" = now() AT TIME ZONE 'utc' WHERE \"CreatedAt\" IS NULL;");
                migrationBuilder.Sql("UPDATE \"TechnicalCategories\" SET \"CreatedByUserId\" = 'system' WHERE \"CreatedByUserId\" IS NULL;");
                migrationBuilder.Sql("UPDATE \"TechnicalCategories\" SET \"CreatedAt\" = now() AT TIME ZONE 'utc' WHERE \"CreatedAt\" IS NULL;");
            }
            else
            {
                migrationBuilder.Sql("UPDATE [ProjectCategories] SET [CreatedByUserId] = 'system' WHERE [CreatedByUserId] IS NULL;");
                migrationBuilder.Sql("UPDATE [ProjectCategories] SET [CreatedAt] = GETUTCDATE() WHERE [CreatedAt] IS NULL;");
                migrationBuilder.Sql("UPDATE [TechnicalCategories] SET [CreatedByUserId] = 'system' WHERE [CreatedByUserId] IS NULL;");
                migrationBuilder.Sql("UPDATE [TechnicalCategories] SET [CreatedAt] = GETUTCDATE() WHERE [CreatedAt] IS NULL;");
            }

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "TechnicalCategories",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "TechnicalCategories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "ProjectCategories",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "ProjectCategories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TechnicalCategories");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "TechnicalCategories");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ProjectCategories");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ProjectCategories");
        }
    }
}
