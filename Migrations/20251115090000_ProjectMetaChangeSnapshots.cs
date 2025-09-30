using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class ProjectMetaChangeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalCaseFileNumber",
                table: "ProjectMetaChangeRequests",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalCategoryId",
                table: "ProjectMetaChangeRequests",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalDescription",
                table: "ProjectMetaChangeRequests",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalName",
                table: "ProjectMetaChangeRequests",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "OriginalRowVersion",
                table: "ProjectMetaChangeRequests",
                maxLength: 8,
                nullable: true);

            if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql(@"
UPDATE r
SET OriginalName = p.Name,
    OriginalDescription = p.Description,
    OriginalCategoryId = p.CategoryId,
    OriginalCaseFileNumber = p.CaseFileNumber,
    OriginalRowVersion = p.RowVersion
FROM ProjectMetaChangeRequests AS r
INNER JOIN Projects AS p ON p.Id = r.ProjectId;");
            }
            else if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql(@"
UPDATE \"ProjectMetaChangeRequests\" AS r
SET \"OriginalName\" = p.\"Name\",
    \"OriginalDescription\" = p.\"Description\",
    \"OriginalCategoryId\" = p.\"CategoryId\",
    \"OriginalCaseFileNumber\" = p.\"CaseFileNumber\",
    \"OriginalRowVersion\" = p.\"RowVersion\"
FROM \"Projects\" AS p
WHERE p.\"Id\" = r.\"ProjectId\";");
            }
            else
            {
                migrationBuilder.Sql(@"
UPDATE ProjectMetaChangeRequests
SET OriginalName = (SELECT Name FROM Projects WHERE Projects.Id = ProjectMetaChangeRequests.ProjectId),
    OriginalDescription = (SELECT Description FROM Projects WHERE Projects.Id = ProjectMetaChangeRequests.ProjectId),
    OriginalCategoryId = (SELECT CategoryId FROM Projects WHERE Projects.Id = ProjectMetaChangeRequests.ProjectId),
    OriginalCaseFileNumber = (SELECT CaseFileNumber FROM Projects WHERE Projects.Id = ProjectMetaChangeRequests.ProjectId),
    OriginalRowVersion = (SELECT RowVersion FROM Projects WHERE Projects.Id = ProjectMetaChangeRequests.ProjectId);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalCaseFileNumber",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropColumn(
                name: "OriginalCategoryId",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropColumn(
                name: "OriginalDescription",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropColumn(
                name: "OriginalName",
                table: "ProjectMetaChangeRequests");

            migrationBuilder.DropColumn(
                name: "OriginalRowVersion",
                table: "ProjectMetaChangeRequests");
        }
    }
}
