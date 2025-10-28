using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityDeleteRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityDeleteRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDeleteRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_ActivityId",
                table: "ActivityDeleteRequests",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_ApprovedByUserId",
                table: "ActivityDeleteRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RequestedAtUtc",
                table: "ActivityDeleteRequests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RequestedByUserId",
                table: "ActivityDeleteRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RejectedByUserId",
                table: "ActivityDeleteRequests",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityDeleteRequests_ActivityId_Pending",
                table: "ActivityDeleteRequests",
                column: "ActivityId",
                unique: true,
                filter: "\"ApprovedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityDeleteRequests");
        }
    }
}
