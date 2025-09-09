using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddCelebrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Celebrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<byte>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PartnerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Day = table.Column<byte>(type: "smallint", nullable: false),
                    Month = table.Column<byte>(type: "smallint", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Celebrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Celebrations_DeletedUtc",
                table: "Celebrations",
                column: "DeletedUtc",
                filter: "\"DeletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Celebrations_EventType_Month_Day",
                table: "Celebrations",
                columns: new[] { "EventType", "Month", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Celebrations");
        }
    }
}
