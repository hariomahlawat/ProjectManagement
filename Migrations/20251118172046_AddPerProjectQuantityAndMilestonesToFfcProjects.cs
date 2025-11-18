using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddPerProjectQuantityAndMilestonesToFfcProjects : Migration
    {
        /// <inheritdoc />
        // SECTION: Up migration
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveredOn",
                table: "FfcProjects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDelivered",
                table: "FfcProjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInstalled",
                table: "FfcProjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InstalledOn",
                table: "FfcProjects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "FfcProjects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FfcProjects_Quantity_Positive",
                table: "FfcProjects",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FfcProjects_DeliveredOn_RequiresFlag",
                table: "FfcProjects",
                sql: "\"DeliveredOn\" IS NULL OR \"IsDelivered\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FfcProjects_InstalledOn_RequiresFlag",
                table: "FfcProjects",
                sql: "\"InstalledOn\" IS NULL OR \"IsInstalled\" = TRUE");

            migrationBuilder.Sql(
                @"
                UPDATE ""FfcProjects"" AS project
                SET ""IsDelivered"" = record.""DeliveryYes"",
                    ""DeliveredOn"" = record.""DeliveryDate"",
                    ""IsInstalled"" = record.""InstallationYes"",
                    ""InstalledOn"" = record.""InstallationDate""
                FROM ""FfcRecords"" AS record
                WHERE project.""FfcRecordId"" = record.""Id"";
                ");

            migrationBuilder.Sql(
                @"
                INSERT INTO ""FfcProjects"" (
                        ""FfcRecordId"",
                        ""Name"",
                        ""Remarks"",
                        ""Quantity"",
                        ""IsDelivered"",
                        ""DeliveredOn"",
                        ""IsInstalled"",
                        ""InstalledOn""
                    )
                    SELECT record.""Id"",
                           'Legacy project entry',
                           'Auto generated during FFC schema upgrade',
                           1,
                           record.""DeliveryYes"",
                           record.""DeliveryDate"",
                           record.""InstallationYes"",
                           record.""InstallationDate""
                    FROM ""FfcRecords"" AS record
                    WHERE (record.""DeliveryYes"" = TRUE OR record.""InstallationYes"" = TRUE)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM ""FfcProjects"" AS project
                          WHERE project.""FfcRecordId"" = record.""Id""
                      );
                ");
        }

        /// <inheritdoc />
        // SECTION: Down migration
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FfcProjects_Quantity_Positive",
                table: "FfcProjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FfcProjects_DeliveredOn_RequiresFlag",
                table: "FfcProjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FfcProjects_InstalledOn_RequiresFlag",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "DeliveredOn",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "IsDelivered",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "IsInstalled",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "InstalledOn",
                table: "FfcProjects");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "FfcProjects");
        }
    }
}
