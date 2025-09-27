using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddStageChangeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

            if (isSqlServer)
            {
                migrationBuilder.CreateTable(
                    name: "StageChangeLogs",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        ProjectId = table.Column<int>(type: "int", nullable: false),
                        StageCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                        Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                        FromStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                        ToStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                        FromActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                        ToActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                        FromCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                        ToCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                        UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                        Note = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_StageChangeLogs", x => x.Id);
                        table.CheckConstraint("CK_StageChangeLogs_Action", "[Action] IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
                    });

                migrationBuilder.CreateTable(
                    name: "StageChangeRequests",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        ProjectId = table.Column<int>(type: "int", nullable: false),
                        StageCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                        RequestedStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                        RequestedDate = table.Column<DateOnly>(type: "date", nullable: true),
                        Note = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                        RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        RequestedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                        DecisionStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false, defaultValue: "Pending"),
                        DecidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        DecidedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                        DecisionNote = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_StageChangeRequests", x => x.Id);
                        table.CheckConstraint("CK_StageChangeRequests_DecisionStatus", "[DecisionStatus] IN ('Pending','Approved','Rejected','Superseded')");
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeLogs_ProjectId_StageCode_At",
                    table: "StageChangeLogs",
                    columns: new[] { "ProjectId", "StageCode", "At" });

                migrationBuilder.Sql(
                    @"CREATE UNIQUE INDEX IX_StageChangeRequests_ProjectId_StageCode_Pending
ON StageChangeRequests(ProjectId, StageCode)
WHERE [DecisionStatus] = 'Pending';");
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "StageChangeLogs",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        ProjectId = table.Column<int>(type: "integer", nullable: false),
                        StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                        FromStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                        ToStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                        FromActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                        ToActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                        FromCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                        ToCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                        UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_StageChangeLogs", x => x.Id);
                        table.CheckConstraint("CK_StageChangeLogs_Action", "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
                    });

                migrationBuilder.CreateTable(
                    name: "StageChangeRequests",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        ProjectId = table.Column<int>(type: "integer", nullable: false),
                        StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        RequestedStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                        RequestedDate = table.Column<DateOnly>(type: "date", nullable: true),
                        Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                        RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        RequestedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        DecisionStatus = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, defaultValue: "Pending"),
                        DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        DecidedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        DecisionNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_StageChangeRequests", x => x.Id);
                        table.CheckConstraint("CK_StageChangeRequests_DecisionStatus", "\"DecisionStatus\" IN ('Pending','Approved','Rejected','Superseded')");
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeLogs_ProjectId_StageCode_At",
                    table: "StageChangeLogs",
                    columns: new[] { "ProjectId", "StageCode", "At" });

                migrationBuilder.CreateIndex(
                    name: "IX_StageChangeRequests_ProjectId_StageCode",
                    table: "StageChangeRequests",
                    columns: new[] { "ProjectId", "StageCode" },
                    unique: true,
                    filter: "\"DecisionStatus\" = 'Pending'");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

            if (isSqlServer)
            {
                migrationBuilder.Sql("DROP INDEX IX_StageChangeRequests_ProjectId_StageCode_Pending ON StageChangeRequests;");
            }

            migrationBuilder.DropTable(
                name: "StageChangeLogs");

            migrationBuilder.DropTable(
                name: "StageChangeRequests");
        }
    }
}
