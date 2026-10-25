using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Adds the Amendment letter issue kind (Kind = 3) to ARPP. Amendment letters are numbered
    /// separately from addenda, so the issue uniqueness key becomes financial year + kind +
    /// sequence, and the kind/sequence check constraints accept the new kind. Existing rows are
    /// unchanged: every Original keeps sequence 0 and every Addendum keeps its number.
    /// </summary>
    public partial class AddArppAmendmentIssueKind : Migration
    {
        private const string PreviousKindSequenceSql =
            "(\"Kind\" = 1 AND \"IssueSequence\" = 0) OR (\"Kind\" = 2 AND \"IssueSequence\" > 0)";

        private const string KindSequenceSql =
            "(\"Kind\" = 1 AND \"IssueSequence\" = 0) OR (\"Kind\" IN (2, 3) AND \"IssueSequence\" > 0)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ArppIssues_KindSequence",
                table: "ArppIssues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ArppIssues_KindSequence",
                table: "ArppIssues",
                sql: KindSequenceSql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ArppPublishedIssues_KindSequence",
                table: "ArppPublishedIssues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ArppPublishedIssues_KindSequence",
                table: "ArppPublishedIssues",
                sql: KindSequenceSql);

            migrationBuilder.DropIndex(
                name: "UX_ArppIssues_FinancialYear_Sequence",
                table: "ArppIssues");

            migrationBuilder.CreateIndex(
                name: "UX_ArppIssues_FinancialYear_Sequence",
                table: "ArppIssues",
                columns: new[] { "FinancialYearStart", "Kind", "IssueSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back is only valid while no Amendment (Kind = 3) rows exist; the
            // constraints below would otherwise reject the existing data.
            migrationBuilder.DropIndex(
                name: "UX_ArppIssues_FinancialYear_Sequence",
                table: "ArppIssues");

            migrationBuilder.CreateIndex(
                name: "UX_ArppIssues_FinancialYear_Sequence",
                table: "ArppIssues",
                columns: new[] { "FinancialYearStart", "IssueSequence" },
                unique: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ArppPublishedIssues_KindSequence",
                table: "ArppPublishedIssues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ArppPublishedIssues_KindSequence",
                table: "ArppPublishedIssues",
                sql: PreviousKindSequenceSql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ArppIssues_KindSequence",
                table: "ArppIssues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ArppIssues_KindSequence",
                table: "ArppIssues",
                sql: PreviousKindSequenceSql);
        }
    }
}
