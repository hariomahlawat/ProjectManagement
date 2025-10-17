using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class UnifyTotRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
INSERT INTO \"Remarks\" (\"ProjectId\", \"AuthorUserId\", \"AuthorRole\", \"Type\", \"Scope\", \"Body\", \"EventDate\", \"CreatedAtUtc\", \"IsDeleted\", \"RowVersion\")
SELECT t.\"ProjectId\",
       COALESCE(NULLIF(t.\"LastApprovedByUserId\", ''), p.\"CreatedByUserId\"),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       BTRIM(t.\"Remarks\"),
       COALESCE(
           t.\"CompletedOn\",
           t.\"StartedOn\",
           CAST(t.\"LastApprovedOnUtc\" AS date),
           p.\"CompletedOn\",
           p.\"CancelledOn\",
           CAST(p.\"CreatedAt\" AS date)
       ),
       COALESCE(t.\"LastApprovedOnUtc\", p.\"CreatedAt\"),
       FALSE,
       decode(md5(random()::text || clock_timestamp()::text), 'hex')
FROM \"ProjectTots\" t
JOIN \"Projects\" p ON p.\"Id\" = t.\"ProjectId\"
WHERE t.\"Remarks\" IS NOT NULL
  AND BTRIM(t.\"Remarks\") <> ''
  AND COALESCE(NULLIF(t.\"LastApprovedByUserId\", ''), p.\"CreatedByUserId\") IS NOT NULL;

INSERT INTO \"Remarks\" (\"ProjectId\", \"AuthorUserId\", \"AuthorRole\", \"Type\", \"Scope\", \"Body\", \"EventDate\", \"CreatedAtUtc\", \"IsDeleted\", \"RowVersion\")
SELECT r.\"ProjectId\",
       COALESCE(NULLIF(r.\"SubmittedByUserId\", ''), p.\"CreatedByUserId\"),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       BTRIM(r.\"ProposedRemarks\"),
       COALESCE(
           r.\"ProposedCompletedOn\",
           r.\"ProposedStartedOn\",
           CAST(r.\"SubmittedOnUtc\" AS date),
           p.\"CompletedOn\",
           p.\"CancelledOn\",
           CAST(p.\"CreatedAt\" AS date)
       ),
       COALESCE(r.\"SubmittedOnUtc\", p.\"CreatedAt\"),
       FALSE,
       decode(md5(random()::text || clock_timestamp()::text), 'hex')
FROM \"ProjectTotRequests\" r
JOIN \"Projects\" p ON p.\"Id\" = r.\"ProjectId\"
WHERE r.\"ProposedRemarks\" IS NOT NULL
  AND BTRIM(r.\"ProposedRemarks\") <> ''
  AND COALESCE(NULLIF(r.\"SubmittedByUserId\", ''), p.\"CreatedByUserId\") IS NOT NULL;

INSERT INTO \"Remarks\" (\"ProjectId\", \"AuthorUserId\", \"AuthorRole\", \"Type\", \"Scope\", \"Body\", \"EventDate\", \"CreatedAtUtc\", \"IsDeleted\", \"RowVersion\")
SELECT r.\"ProjectId\",
       COALESCE(NULLIF(r.\"DecidedByUserId\", ''), NULLIF(r.\"SubmittedByUserId\", ''), p.\"CreatedByUserId\"),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       BTRIM(r.\"DecisionRemarks\"),
       COALESCE(
           CAST(r.\"DecidedOnUtc\" AS date),
           CAST(r.\"SubmittedOnUtc\" AS date),
           p.\"CompletedOn\",
           p.\"CancelledOn\",
           CAST(p.\"CreatedAt\" AS date)
       ),
       COALESCE(r.\"DecidedOnUtc\", r.\"SubmittedOnUtc\", p.\"CreatedAt\"),
       FALSE,
       decode(md5(random()::text || clock_timestamp()::text), 'hex')
FROM \"ProjectTotRequests\" r
JOIN \"Projects\" p ON p.\"Id\" = r.\"ProjectId\"
WHERE r.\"DecisionRemarks\" IS NOT NULL
  AND BTRIM(r.\"DecisionRemarks\") <> ''
  AND COALESCE(NULLIF(r.\"DecidedByUserId\", ''), NULLIF(r.\"SubmittedByUserId\", ''), p.\"CreatedByUserId\") IS NOT NULL;
");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
INSERT INTO [Remarks] ([ProjectId], [AuthorUserId], [AuthorRole], [Type], [Scope], [Body], [EventDate], [CreatedAtUtc], [IsDeleted], [RowVersion])
SELECT t.[ProjectId],
       COALESCE(NULLIF(t.[LastApprovedByUserId], ''), p.[CreatedByUserId]),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       LTRIM(RTRIM(t.[Remarks])),
       COALESCE(
           t.[CompletedOn],
           t.[StartedOn],
           CONVERT(date, t.[LastApprovedOnUtc]),
           p.[CompletedOn],
           p.[CancelledOn],
           CONVERT(date, p.[CreatedAt])
       ),
       COALESCE(t.[LastApprovedOnUtc], p.[CreatedAt]),
       CAST(0 AS bit),
       CONVERT(varbinary(16), NEWID())
FROM [ProjectTots] t
JOIN [Projects] p ON p.[Id] = t.[ProjectId]
WHERE t.[Remarks] IS NOT NULL
  AND LTRIM(RTRIM(t.[Remarks])) <> ''
  AND COALESCE(NULLIF(t.[LastApprovedByUserId], ''), p.[CreatedByUserId]) IS NOT NULL;

INSERT INTO [Remarks] ([ProjectId], [AuthorUserId], [AuthorRole], [Type], [Scope], [Body], [EventDate], [CreatedAtUtc], [IsDeleted], [RowVersion])
SELECT r.[ProjectId],
       COALESCE(NULLIF(r.[SubmittedByUserId], ''), p.[CreatedByUserId]),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       LTRIM(RTRIM(r.[ProposedRemarks])),
       COALESCE(
           r.[ProposedCompletedOn],
           r.[ProposedStartedOn],
           CONVERT(date, r.[SubmittedOnUtc]),
           p.[CompletedOn],
           p.[CancelledOn],
           CONVERT(date, p.[CreatedAt])
       ),
       COALESCE(r.[SubmittedOnUtc], p.[CreatedAt]),
       CAST(0 AS bit),
       CONVERT(varbinary(16), NEWID())
FROM [ProjectTotRequests] r
JOIN [Projects] p ON p.[Id] = r.[ProjectId]
WHERE r.[ProposedRemarks] IS NOT NULL
  AND LTRIM(RTRIM(r.[ProposedRemarks])) <> ''
  AND COALESCE(NULLIF(r.[SubmittedByUserId], ''), p.[CreatedByUserId]) IS NOT NULL;

INSERT INTO [Remarks] ([ProjectId], [AuthorUserId], [AuthorRole], [Type], [Scope], [Body], [EventDate], [CreatedAtUtc], [IsDeleted], [RowVersion])
SELECT r.[ProjectId],
       COALESCE(NULLIF(r.[DecidedByUserId], ''), NULLIF(r.[SubmittedByUserId], ''), p.[CreatedByUserId]),
       'Unknown',
       'Internal',
       'TransferOfTechnology',
       LTRIM(RTRIM(r.[DecisionRemarks])),
       COALESCE(
           CONVERT(date, r.[DecidedOnUtc]),
           CONVERT(date, r.[SubmittedOnUtc]),
           p.[CompletedOn],
           p.[CancelledOn],
           CONVERT(date, p.[CreatedAt])
       ),
       COALESCE(r.[DecidedOnUtc], r.[SubmittedOnUtc], p.[CreatedAt]),
       CAST(0 AS bit),
       CONVERT(varbinary(16), NEWID())
FROM [ProjectTotRequests] r
JOIN [Projects] p ON p.[Id] = r.[ProjectId]
WHERE r.[DecisionRemarks] IS NOT NULL
  AND LTRIM(RTRIM(r.[DecisionRemarks])) <> ''
  AND COALESCE(NULLIF(r.[DecidedByUserId], ''), NULLIF(r.[SubmittedByUserId], ''), p.[CreatedByUserId]) IS NOT NULL;
");
            }

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "ProjectTots");

            migrationBuilder.DropColumn(
                name: "ProposedRemarks",
                table: "ProjectTotRequests");

            migrationBuilder.DropColumn(
                name: "DecisionRemarks",
                table: "ProjectTotRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "ProjectTots",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedRemarks",
                table: "ProjectTotRequests",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionRemarks",
                table: "ProjectTotRequests",
                maxLength: 2000,
                nullable: true);
        }
    }
}
