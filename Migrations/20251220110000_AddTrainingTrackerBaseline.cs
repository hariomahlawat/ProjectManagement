using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingTrackerBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrainingRankCategoryMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Rank = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRankCategoryMap", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TrainingMonth = table.Column<int>(type: "integer", nullable: true),
                    TrainingYear = table.Column<int>(type: "integer", nullable: true),
                    LegacyOfficerCount = table.Column<int>(type: "integer", nullable: false),
                    LegacyJcoCount = table.Column<int>(type: "integer", nullable: false),
                    LegacyOrCount = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trainings_TrainingTypes_TrainingTypeId",
                        column: x => x.TrainingTypeId,
                        principalTable: "TrainingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCounters",
                columns: table => new
                {
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Officers = table.Column<int>(type: "integer", nullable: false),
                    JuniorCommissionedOfficers = table.Column<int>(type: "integer", nullable: false),
                    OtherRanks = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCounters", x => x.TrainingId);
                    table.ForeignKey(
                        name: "FK_TrainingCounters_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingDeleteRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingDeleteRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingDeleteRequests_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingProjects",
                columns: table => new
                {
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    AllocationShare = table.Column<decimal>(type: "numeric(9,4)", nullable: false, defaultValue: 0m),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingProjects", x => new { x.TrainingId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_TrainingProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingProjects_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "TrainingTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "DisplayOrder", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { new Guid("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", "Simulator-based training sessions.", 1, true, null, null, "Simulator", new byte[] { 77, 155, 111, 117, 141, 150, 71, 212, 157, 65, 159, 79, 74, 14, 166, 121 } },
                    { new Guid("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", "Drone operator and maintenance training.", 2, true, null, null, "Drone", new byte[] { 210, 243, 145, 188, 100, 164, 76, 54, 146, 24, 26, 59, 169, 189, 234, 249 } }
                });

            migrationBuilder.InsertData(
                table: "TrainingRankCategoryMap",
                columns: new[] { "Id", "Category", "CreatedAtUtc", "CreatedByUserId", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Rank", "RowVersion" },
                values: new object[,]
                {
                    { 1, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Lt", new byte[] { 95, 111, 159, 61, 34, 255, 74, 143, 158, 242, 124, 179, 241, 169, 5, 19 } },
                    { 2, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Capt", new byte[] { 214, 205, 160, 166, 106, 86, 75, 52, 157, 190, 166, 144, 181, 140, 219, 223 } },
                    { 3, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Maj", new byte[] { 86, 101, 141, 138, 29, 59, 77, 154, 191, 53, 248, 240, 167, 241, 218, 107 } },
                    { 4, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Lt Col", new byte[] { 167, 155, 39, 52, 55, 206, 74, 160, 143, 167, 30, 67, 213, 198, 166, 244 } },
                    { 5, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Col", new byte[] { 30, 252, 117, 122, 4, 252, 78, 34, 134, 229, 44, 201, 190, 58, 129, 215 } },
                    { 6, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Brig", new byte[] { 168, 246, 169, 199, 95, 209, 76, 83, 142, 42, 194, 185, 241, 176, 240, 215 } },
                    { 7, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Maj Gen", new byte[] { 45, 159, 18, 52, 51, 142, 77, 74, 191, 103, 58, 193, 48, 150, 164, 184 } },
                    { 8, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Lt Gen", new byte[] { 79, 139, 41, 28, 47, 38, 75, 105, 155, 111, 67, 59, 33, 22, 177, 217 } },
                    { 9, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Gen", new byte[] { 201, 192, 33, 186, 145, 174, 66, 193, 185, 232, 147, 14, 16, 183, 196, 126 } },
                    { 10, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Naib Subedar", new byte[] { 245, 215, 166, 120, 210, 236, 77, 75, 164, 229, 44, 77, 86, 243, 241, 180 } },
                    { 11, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Subedar", new byte[] { 166, 22, 137, 176, 68, 168, 71, 64, 148, 81, 162, 169, 99, 159, 77, 157 } },
                    { 12, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Subedar Major", new byte[] { 227, 194, 181, 217, 18, 175, 71, 172, 182, 159, 104, 232, 230, 245, 211, 193 } },
                    { 13, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Sepoy", new byte[] { 10, 125, 47, 94, 187, 180, 75, 216, 183, 61, 148, 165, 8, 42, 77, 12 } },
                    { 14, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Lance Naik", new byte[] { 154, 159, 76, 142, 10, 18, 78, 95, 144, 27, 151, 20, 252, 183, 217, 194 } },
                    { 15, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Naik", new byte[] { 178, 229, 247, 195, 125, 21, 78, 226, 155, 241, 12, 104, 75, 39, 220, 233 } },
                    { 16, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero), "system", true, null, null, "Havildar", new byte[] { 141, 150, 242, 188, 18, 212, 67, 244, 141, 107, 43, 176, 121, 72, 243, 217 } }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRankCategoryMap_Rank",
                table: "TrainingRankCategoryMap",
                column: "Rank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDeleteRequests_Status",
                table: "TrainingDeleteRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDeleteRequests_TrainingId",
                table: "TrainingDeleteRequests",
                column: "TrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingProjects_ProjectId",
                table: "TrainingProjects",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTypes_Name",
                table: "TrainingTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_EndDate",
                table: "Trainings",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_StartDate",
                table: "Trainings",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_TrainingTypeId",
                table: "Trainings",
                column: "TrainingTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_TrainingYear",
                table: "Trainings",
                column: "TrainingYear");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingCounters");

            migrationBuilder.DropTable(
                name: "TrainingDeleteRequests");

            migrationBuilder.DropTable(
                name: "TrainingProjects");

            migrationBuilder.DropTable(
                name: "TrainingRankCategoryMap");

            migrationBuilder.DropTable(
                name: "Trainings");

            migrationBuilder.DropTable(
                name: "TrainingTypes");
        }
    }
}
