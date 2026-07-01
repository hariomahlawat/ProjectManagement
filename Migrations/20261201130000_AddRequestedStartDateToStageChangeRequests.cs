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

            // Preserve start-date semantics for completion proposals created before this
            // column existed. The SQL is provider-specific because production uses PostgreSQL
            // while lightweight tests may use SQLite and some tooling may target SQL Server.
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
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
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE request
                    SET request.[RequestedStartDate] = COALESCE(stage.[ActualStart], earlier.[RequestedDate])
                    FROM [StageChangeRequests] AS request
                    INNER JOIN [ProjectStages] AS stage
                        ON request.[ProjectId] = stage.[ProjectId]
                       AND request.[StageCode] = stage.[StageCode]
                    OUTER APPLY (
                        SELECT TOP (1) candidate.[RequestedDate]
                        FROM [StageChangeRequests] AS candidate
                        WHERE candidate.[ProjectId] = request.[ProjectId]
                          AND candidate.[StageCode] = request.[StageCode]
                          AND candidate.[RequestedStatus] = 'InProgress'
                          AND candidate.[RequestedDate] IS NOT NULL
                          AND (
                              candidate.[RequestedOn] < request.[RequestedOn]
                              OR (
                                  candidate.[RequestedOn] = request.[RequestedOn]
                                  AND candidate.[Id] < request.[Id]
                              )
                          )
                        ORDER BY candidate.[RequestedOn] DESC, candidate.[Id] DESC
                    ) AS earlier
                    WHERE request.[RequestedStatus] = 'Completed'
                      AND request.[RequestedStartDate] IS NULL;
                    """);
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "StageChangeRequests"
                    SET "RequestedStartDate" = COALESCE(
                        (
                            SELECT stage."ActualStart"
                            FROM "ProjectStages" AS stage
                            WHERE stage."ProjectId" = "StageChangeRequests"."ProjectId"
                              AND stage."StageCode" = "StageChangeRequests"."StageCode"
                            LIMIT 1
                        ),
                        (
                            SELECT earlier."RequestedDate"
                            FROM "StageChangeRequests" AS earlier
                            WHERE earlier."ProjectId" = "StageChangeRequests"."ProjectId"
                              AND earlier."StageCode" = "StageChangeRequests"."StageCode"
                              AND earlier."RequestedStatus" = 'InProgress'
                              AND earlier."RequestedDate" IS NOT NULL
                              AND (
                                  earlier."RequestedOn" < "StageChangeRequests"."RequestedOn"
                                  OR (
                                      earlier."RequestedOn" = "StageChangeRequests"."RequestedOn"
                                      AND earlier."Id" < "StageChangeRequests"."Id"
                                  )
                              )
                            ORDER BY earlier."RequestedOn" DESC, earlier."Id" DESC
                            LIMIT 1
                        )
                    )
                    WHERE "RequestedStatus" = 'Completed'
                      AND "RequestedStartDate" IS NULL;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedStartDate",
                table: "StageChangeRequests");
        }
    }
}
