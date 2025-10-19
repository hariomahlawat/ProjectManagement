using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class Add_ProliferationRefactor_MVP : Migration
    {
        protected override void Up(MigrationBuilder m)
        {
            m.CreateTable(
                name: "ProliferationYearly",
                columns: t => new
                {
                    Id = t.Column<Guid>(nullable: false),
                    ProjectId = t.Column<int>(nullable: false),
                    Source = t.Column<int>(nullable: false),
                    Year = t.Column<int>(nullable: false),
                    TotalQuantity = t.Column<int>(nullable: false),
                    Remarks = t.Column<string>(maxLength: 500, nullable: true),
                    ApprovalStatus = t.Column<int>(nullable: false),
                    SubmittedByUserId = t.Column<string>(nullable: false),
                    ApprovedByUserId = t.Column<string>(nullable: true),
                    ApprovedOnUtc = t.Column<DateTime>(nullable: true),
                    CreatedOnUtc = t.Column<DateTime>(nullable: false),
                    LastUpdatedOnUtc = t.Column<DateTime>(nullable: false),
                    RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: t => { t.PrimaryKey("PK_ProliferationYearly", x => x.Id); });

            m.CreateTable(
                name: "ProliferationGranular",
                columns: t => new
                {
                    Id = t.Column<Guid>(nullable: false),
                    ProjectId = t.Column<int>(nullable: false),
                    Source = t.Column<int>(nullable: false),
                    SimulatorName = t.Column<string>(maxLength: 200, nullable: false),
                    UnitName = t.Column<string>(maxLength: 200, nullable: false),
                    ProliferationDate = t.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = t.Column<int>(nullable: false),
                    Remarks = t.Column<string>(maxLength: 500, nullable: true),
                    ApprovalStatus = t.Column<int>(nullable: false),
                    SubmittedByUserId = t.Column<string>(nullable: false),
                    ApprovedByUserId = t.Column<string>(nullable: true),
                    ApprovedOnUtc = t.Column<DateTime>(nullable: true),
                    CreatedOnUtc = t.Column<DateTime>(nullable: false),
                    LastUpdatedOnUtc = t.Column<DateTime>(nullable: false),
                    RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: t => { t.PrimaryKey("PK_ProliferationGranular", x => x.Id); });

            m.CreateTable(
                name: "ProliferationYearPreference",
                columns: t => new
                {
                    Id = t.Column<Guid>(nullable: false),
                    ProjectId = t.Column<int>(nullable: false),
                    Source = t.Column<int>(nullable: false),
                    Year = t.Column<int>(nullable: false),
                    Mode = t.Column<int>(nullable: false),
                    SetByUserId = t.Column<string>(nullable: false),
                    SetOnUtc = t.Column<DateTime>(nullable: false)
                },
                constraints: t => { t.PrimaryKey("PK_ProliferationYearPreference", x => x.Id); });

            m.Sql(@"
CREATE VIEW ""vw_ProliferationGranularYearly"" AS
SELECT
  ""ProjectId"",
  ""Source"",
  EXTRACT(YEAR FROM ""ProliferationDate"")::int AS ""Year"",
  COALESCE(SUM(""Quantity""), 0) AS ""TotalQuantity""
FROM ""ProliferationGranular""
WHERE ""ApprovalStatus"" = 1
GROUP BY ""ProjectId"", ""Source"", EXTRACT(YEAR FROM ""ProliferationDate"");
");

            m.CreateIndex("IX_ProlifYearly_Project_Source_Year", "ProliferationYearly", new[] { "ProjectId", "Source", "Year" });
            m.CreateIndex("IX_ProlifGranular_Project_Source_Date", "ProliferationGranular", new[] { "ProjectId", "Source", "ProliferationDate" });
            m.CreateIndex("UX_ProlifYearPref_Project_Source_Year", "ProliferationYearPreference", new[] { "ProjectId", "Source", "Year" }, unique: true);

            // If old proliferation tables exist, leave them for now. We will delete them at the end of the rollout.
        }

        protected override void Down(MigrationBuilder m)
        {
            m.Sql(@"DROP VIEW IF EXISTS ""vw_ProliferationGranularYearly"";");
            m.DropTable("ProliferationYearPreference");
            m.DropTable("ProliferationGranular");
            m.DropTable("ProliferationYearly");
        }
    }
}
