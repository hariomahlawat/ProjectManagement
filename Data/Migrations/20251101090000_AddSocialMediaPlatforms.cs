using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialMediaPlatforms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.CreateTable(
                    name: "SocialMediaPlatforms",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                        CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaPlatforms", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaPlatforms_Name",
                    table: "SocialMediaPlatforms",
                    column: "Name",
                    unique: true);
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "SocialMediaPlatforms",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                        Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                        IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                        CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                        LastModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        RowVersion = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaPlatforms", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaPlatforms_Name",
                    table: "SocialMediaPlatforms",
                    column: "Name",
                    unique: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialMediaPlatforms");
        }
    }
}
