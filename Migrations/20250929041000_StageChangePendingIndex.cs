using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class StageChangePendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.DropIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "[DecisionStatus] = 'Pending'");
            }
            else if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.DropIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "\"DecisionStatus\" = 'Pending'");
            }
            else
            {
                migrationBuilder.DropIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "DecisionStatus = 'Pending'");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.DropIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "[DecisionStatus] = 'Pending'");
            }
            else if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.DropIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "\"DecisionStatus\" = 'Pending'");
            }
            else
            {
                migrationBuilder.DropIndex(
                    name: "ux_stagechangerequests_pending",
                    table: "StageChangeRequests");

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "DecisionStatus = 'Pending'");
            }
        }
    }
}
