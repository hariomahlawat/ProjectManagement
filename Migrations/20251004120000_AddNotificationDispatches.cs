using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddNotificationDispatches : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDispatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DispatchedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_DispatchedUtc",
                table: "NotificationDispatches",
                column: "DispatchedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_RecipientUserId_Kind_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "RecipientUserId", "Kind", "DispatchedUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDispatches");
        }
    }
}
