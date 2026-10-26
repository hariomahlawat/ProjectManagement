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

            // At most one pending amendment (ApprovalStatus 0) per approved record.
            migrationBuilder.CreateIndex(
                name: "UX_ProlifYearly_PendingAmendment",
                table: "ProliferationYearly",
                column: "AmendsId",
                unique: true,
                filter: "\"AmendsId\" IS NOT NULL AND \"ApprovalStatus\" = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProlifGranular_PendingAmendment",
                table: "ProliferationGranular",
                column: "AmendsId",
                unique: true,
                filter: "\"AmendsId\" IS NOT NULL AND \"ApprovalStatus\" = 0");

            // Deleting a record deletes its amendments, and an amendment cannot be filed
            // against a record that no longer exists.
            migrationBuilder.AddForeignKey(
                name: "FK_ProliferationYearly_ProliferationYearly_AmendsId",
                table: "ProliferationYearly",
                column: "AmendsId",
                principalTable: "ProliferationYearly",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProliferationGranular_ProliferationGranular_AmendsId",
                table: "ProliferationGranular",
                column: "AmendsId",
                principalTable: "ProliferationGranular",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProliferationYearly_ProliferationYearly_AmendsId",
                table: "ProliferationYearly");

            migrationBuilder.DropForeignKey(
                name: "FK_ProliferationGranular_ProliferationGranular_AmendsId",
                table: "ProliferationGranular");

            migrationBuilder.DropIndex(
                name: "UX_ProlifYearly_PendingAmendment",
                table: "ProliferationYearly");

            migrationBuilder.DropIndex(
                name: "UX_ProlifGranular_PendingAmendment",
                table: "ProliferationGranular");

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
