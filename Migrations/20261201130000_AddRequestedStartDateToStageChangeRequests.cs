using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201130000_AddRequestedStartDateToStageChangeRequests")]
    public partial class AddRequestedStartDateToStageChangeRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedStartDate",
                table: "StageChangeRequests",
                type: "date",
                nullable: true);

            // Preserve the start-date semantics of completion proposals created
            // before this column existed. Approved stage data is authoritative;
            // otherwise carry the latest earlier InProgress proposal for the same
            // project and stage. New proposals explicitly persist the editable field.
            migrationBuilder.Sql(
                """
                UPDATE "StageChangeRequests" AS request
                SET "RequestedStartDate" = COALESCE(
                    stage."ActualStart",
                    (
                        SELECT earlier."RequestedDate"
                        FROM "StageChangeRequests" AS earlier
                        WHERE earlier."ProjectId" = request."ProjectId"
                          AND earlier."StageCode" = request."StageCode"
                          AND earlier."RequestedStatus" = 'InProgress'
                          AND earlier."RequestedDate" IS NOT NULL
                          AND (
                              earlier."RequestedOn" < request."RequestedOn"
                              OR (
                                  earlier."RequestedOn" = request."RequestedOn"
                                  AND earlier."Id" < request."Id"
                              )
                          )
                        ORDER BY earlier."RequestedOn" DESC, earlier."Id" DESC
                        LIMIT 1
                    )
                )
                FROM "ProjectStages" AS stage
                WHERE request."ProjectId" = stage."ProjectId"
                  AND request."StageCode" = stage."StageCode"
                  AND request."RequestedStatus" = 'Completed'
                  AND request."RequestedStartDate" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedStartDate",
                table: "StageChangeRequests");
        }
    }
}
