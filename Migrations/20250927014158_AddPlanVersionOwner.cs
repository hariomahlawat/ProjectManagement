using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddPlanVersionOwner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

            if (isSqlServer)
            {
                migrationBuilder.AddColumn<string>(
                    name: "OwnerUserId",
                    table: "PlanVersions",
                    type: "nvarchar(450)",
                    maxLength: 450,
                    nullable: true);
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "OwnerUserId",
                    table: "PlanVersions",
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true);
            }

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_OwnerUserId",
                table: "PlanVersions",
                column: "OwnerUserId");

            if (isSqlServer)
            {
                migrationBuilder.Sql(
                    @"CREATE UNIQUE INDEX IX_PlanVersions_Project_User_Draft
ON PlanVersions(ProjectId, OwnerUserId)
WHERE [Status] = 'Draft' AND [OwnerUserId] IS NOT NULL;");
            }
            else
            {
                migrationBuilder.CreateIndex(
                    name: "IX_PlanVersions_Project_User_Draft",
                    table: "PlanVersions",
                    columns: new[] { "ProjectId", "OwnerUserId" },
                    unique: true,
                    filter: "\"Status\" = 'Draft' AND \"OwnerUserId\" IS NOT NULL");
            }

            migrationBuilder.AddForeignKey(
                name: "FK_PlanVersions_AspNetUsers_OwnerUserId",
                table: "PlanVersions",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            if (isSqlServer)
            {
                migrationBuilder.Sql(@"
UPDATE pv
SET OwnerUserId = p.LeadPoUserId
FROM PlanVersions AS pv
INNER JOIN Projects AS p ON pv.ProjectId = p.Id
WHERE pv.Status = 'Draft'
  AND pv.OwnerUserId IS NULL
  AND p.LeadPoUserId IS NOT NULL;
");
            }
            else
            {
                migrationBuilder.Sql(@"
UPDATE ""PlanVersions"" pv
SET ""OwnerUserId"" = p.""LeadPoUserId""
FROM ""Projects"" p
WHERE pv.""ProjectId"" = p.""Id""
  AND pv.""Status"" = 'Draft'
  AND pv.""OwnerUserId"" IS NULL
  AND p.""LeadPoUserId"" IS NOT NULL;
");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

            migrationBuilder.DropForeignKey(
                name: "FK_PlanVersions_AspNetUsers_OwnerUserId",
                table: "PlanVersions");

            migrationBuilder.DropIndex(
                name: "IX_PlanVersions_OwnerUserId",
                table: "PlanVersions");

            if (isSqlServer)
            {
                migrationBuilder.Sql("DROP INDEX IX_PlanVersions_Project_User_Draft ON PlanVersions;");
            }
            else
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanVersions_Project_User_Draft",
                    table: "PlanVersions");
            }

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "PlanVersions");
        }
    }
}
