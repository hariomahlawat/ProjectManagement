using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations;

public partial class AddMetAndFopmToProjectTot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MetDetails",
            table: "ProjectTots",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "MetCompletedOn",
            table: "ProjectTots",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "FirstProductionModelManufactured",
            table: "ProjectTots",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "FirstProductionModelManufacturedOn",
            table: "ProjectTots",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProposedMetDetails",
            table: "ProjectTotRequests",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ProposedMetCompletedOn",
            table: "ProjectTotRequests",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ProposedFirstProductionModelManufactured",
            table: "ProjectTotRequests",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ProposedFirstProductionModelManufacturedOn",
            table: "ProjectTotRequests",
            type: "date",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MetDetails",
            table: "ProjectTots");

        migrationBuilder.DropColumn(
            name: "MetCompletedOn",
            table: "ProjectTots");

        migrationBuilder.DropColumn(
            name: "FirstProductionModelManufactured",
            table: "ProjectTots");

        migrationBuilder.DropColumn(
            name: "FirstProductionModelManufacturedOn",
            table: "ProjectTots");

        migrationBuilder.DropColumn(
            name: "ProposedMetDetails",
            table: "ProjectTotRequests");

        migrationBuilder.DropColumn(
            name: "ProposedMetCompletedOn",
            table: "ProjectTotRequests");

        migrationBuilder.DropColumn(
            name: "ProposedFirstProductionModelManufactured",
            table: "ProjectTotRequests");

        migrationBuilder.DropColumn(
            name: "ProposedFirstProductionModelManufacturedOn",
            table: "ProjectTotRequests");
    }
}
