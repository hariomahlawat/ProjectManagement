using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class LoginAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    WhenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLoginStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLoginStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthEvents_Event_WhenUtc",
                table: "AuthEvents",
                columns: new[] { "Event", "WhenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyLoginStats_Date",
                table: "DailyLoginStats",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthEvents");

            migrationBuilder.DropTable(
                name: "DailyLoginStats");
        }
    }
}
