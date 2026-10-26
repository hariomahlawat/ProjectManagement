using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Adds a nullable link from a proliferation amendment to the approved record it amends.
    /// Additive only: existing rows keep a null link and no data is changed.
    /// </summary>
    public partial class AddProliferationAmendmentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AmendsId",
                table: "ProliferationYearly",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AmendsId",
                table: "ProliferationGranular",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProlifYearly_AmendsId",
                table: "ProliferationYearly",
                column: "AmendsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProlifGranular_AmendsId",
                table: "ProliferationGranular",
                column: "AmendsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProlifYearly_AmendsId",
                table: "ProliferationYearly");

            migrationBuilder.DropIndex(
                name: "IX_ProlifGranular_AmendsId",
                table: "ProliferationGranular");

            migrationBuilder.DropColumn(
                name: "AmendsId",
                table: "ProliferationYearly");

            migrationBuilder.DropColumn(
                name: "AmendsId",
                table: "ProliferationGranular");
        }
    }
}
