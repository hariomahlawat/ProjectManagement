using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207170000_RedesignProcurementJourney")]
public partial class RedesignProcurementJourney : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Purpose",
            table: "StageChecklistTemplates",
            type: "character varying(600)",
            maxLength: 600,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PurposeUpdatedOn",
            table: "StageChecklistTemplates",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PurposeUpdatedByUserId",
            table: "StageChecklistTemplates",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "StageChecklistTemplates"
            SET "Purpose" = CASE UPPER("StageCode")
                WHEN 'FS' THEN 'Establish the operational need, feasibility, broad scope, stakeholders and indicative resources.'
                WHEN 'SOW' THEN 'Define and vet the technical scope, deliverables, standards, acceptance criteria and responsibilities.'
                WHEN 'IPA' THEN 'Obtain in-principle approval to progress the proposal for detailed processing and costing.'
                WHEN 'AON' THEN 'Secure formal acceptance of necessity or sanction for procurement and associated expenditure.'
                WHEN 'BID' THEN 'Publish the approved tender package and manage bidder communication, clarifications and submissions.'
                WHEN 'TEC' THEN 'Evaluate technical compliance, capability, demonstrations and mandatory documentation.'
                WHEN 'BM' THEN 'Establish an independent and defensible benchmark for assessing price reasonableness.'
                WHEN 'COB' THEN 'Open the commercial bids of technically qualified firms and establish the commercial position.'
                WHEN 'PNC' THEN 'Conduct price negotiations where authorised and record the basis for the negotiated outcome.'
                WHEN 'EAS' THEN 'Obtain expenditure approval or financial sanction based on the evaluated commercial proposal.'
                WHEN 'SO' THEN 'Issue the supply order or contract with approved terms, milestones and obligations.'
                WHEN 'DEVP' THEN 'Execute development, integration, reviews and milestone monitoring against the contracted scope.'
                WHEN 'ATP' THEN 'Verify the delivered system against approved acceptance test procedures and contractual criteria.'
                WHEN 'PAYMENT' THEN 'Process payment against accepted deliverables, contractual milestones and supporting documents.'
                WHEN 'TOT' THEN 'Complete the approved transfer of technology, knowledge, documentation and sustainment arrangements.'
                ELSE "Purpose"
            END
            WHERE "Purpose" IS NULL OR BTRIM("Purpose") = '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_StageChecklistTemplates_PurposeUpdatedByUserId",
            table: "StageChecklistTemplates",
            column: "PurposeUpdatedByUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_StageChecklistTemplates_AspNetUsers_PurposeUpdatedByUserId",
            table: "StageChecklistTemplates",
            column: "PurposeUpdatedByUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        // Correct the current procurement graph even when startup seeders are disabled.
        // Technical Evaluation and Benchmarking both start after Bid Process, then
        // converge as mandatory prerequisites for Commercial Opening.
        migrationBuilder.Sql(
            """
            UPDATE "StageTemplates"
            SET "ParallelGroup" = 'PRE_COB'
            WHERE "Version" = 'SDD-2.0'
              AND UPPER("Code") IN ('TEC', 'BM');

            DELETE FROM "StageDependencyTemplates"
            WHERE "Version" = 'SDD-2.0'
              AND UPPER("FromStageCode") = 'BM'
              AND UPPER("DependsOnStageCode") = 'TEC';

            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT 'SDD-2.0', 'BM', 'BID'
            WHERE NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = 'SDD-2.0'
                  AND UPPER("FromStageCode") = 'BM'
                  AND UPPER("DependsOnStageCode") = 'BID'
            );

            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT 'SDD-2.0', 'COB', 'TEC'
            WHERE NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = 'SDD-2.0'
                  AND UPPER("FromStageCode") = 'COB'
                  AND UPPER("DependsOnStageCode") = 'TEC'
            );

            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT 'SDD-2.0', 'COB', 'BM'
            WHERE NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = 'SDD-2.0'
                  AND UPPER("FromStageCode") = 'COB'
                  AND UPPER("DependsOnStageCode") = 'BM'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "StageTemplates"
            SET "ParallelGroup" = NULL
            WHERE "Version" = 'SDD-2.0'
              AND UPPER("Code") IN ('TEC', 'BM');

            DELETE FROM "StageDependencyTemplates"
            WHERE "Version" = 'SDD-2.0'
              AND (
                    (UPPER("FromStageCode") = 'BM' AND UPPER("DependsOnStageCode") = 'BID')
                 OR (UPPER("FromStageCode") = 'COB' AND UPPER("DependsOnStageCode") = 'TEC')
              );

            INSERT INTO "StageDependencyTemplates" ("Version", "FromStageCode", "DependsOnStageCode")
            SELECT 'SDD-2.0', 'BM', 'TEC'
            WHERE NOT EXISTS (
                SELECT 1
                FROM "StageDependencyTemplates"
                WHERE "Version" = 'SDD-2.0'
                  AND UPPER("FromStageCode") = 'BM'
                  AND UPPER("DependsOnStageCode") = 'TEC'
            );
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_StageChecklistTemplates_AspNetUsers_PurposeUpdatedByUserId",
            table: "StageChecklistTemplates");

        migrationBuilder.DropIndex(
            name: "IX_StageChecklistTemplates_PurposeUpdatedByUserId",
            table: "StageChecklistTemplates");

        migrationBuilder.DropColumn(
            name: "Purpose",
            table: "StageChecklistTemplates");

        migrationBuilder.DropColumn(
            name: "PurposeUpdatedOn",
            table: "StageChecklistTemplates");

        migrationBuilder.DropColumn(
            name: "PurposeUpdatedByUserId",
            table: "StageChecklistTemplates");
    }
}
