using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddMiscActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiscActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nomenclature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ExternalLink = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CapturedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiscActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiscActivities_ActivityTypes_ActivityTypeId",
                        column: x => x.ActivityTypeId,
                        principalTable: "ActivityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActivityMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Caption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityMedia_MiscActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "MiscActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 1,
                column: "RowVersion",
                value: new byte[] { 61, 159, 111, 95, 255, 34, 143, 74, 158, 242, 124, 179, 241, 169, 5, 19 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 2,
                column: "RowVersion",
                value: new byte[] { 166, 160, 205, 214, 86, 106, 52, 75, 157, 190, 166, 144, 181, 140, 219, 223 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 3,
                column: "RowVersion",
                value: new byte[] { 138, 141, 101, 86, 59, 29, 154, 77, 191, 53, 248, 240, 167, 241, 218, 107 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 4,
                column: "RowVersion",
                value: new byte[] { 52, 39, 155, 167, 206, 55, 160, 74, 143, 167, 30, 67, 213, 198, 166, 244 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 5,
                column: "RowVersion",
                value: new byte[] { 122, 117, 252, 30, 252, 4, 34, 78, 134, 229, 44, 201, 190, 58, 129, 215 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 6,
                column: "RowVersion",
                value: new byte[] { 199, 169, 246, 168, 209, 95, 83, 76, 142, 42, 194, 185, 241, 176, 240, 215 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 7,
                column: "RowVersion",
                value: new byte[] { 52, 18, 159, 45, 142, 51, 74, 77, 191, 103, 58, 193, 48, 150, 164, 184 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 8,
                column: "RowVersion",
                value: new byte[] { 28, 41, 139, 79, 38, 47, 105, 75, 155, 111, 67, 59, 33, 22, 177, 217 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 9,
                column: "RowVersion",
                value: new byte[] { 186, 33, 192, 201, 174, 145, 193, 66, 185, 232, 147, 14, 16, 183, 196, 126 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 10,
                column: "RowVersion",
                value: new byte[] { 120, 166, 215, 245, 236, 210, 75, 77, 164, 229, 44, 77, 86, 243, 241, 180 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 11,
                column: "RowVersion",
                value: new byte[] { 176, 137, 22, 166, 168, 68, 64, 71, 148, 81, 162, 169, 99, 159, 77, 157 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 12,
                column: "RowVersion",
                value: new byte[] { 217, 181, 194, 227, 175, 18, 172, 71, 182, 159, 104, 232, 230, 245, 211, 193 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 13,
                column: "RowVersion",
                value: new byte[] { 94, 47, 125, 10, 180, 187, 216, 75, 183, 61, 148, 165, 8, 42, 77, 12 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 14,
                column: "RowVersion",
                value: new byte[] { 142, 76, 159, 154, 18, 10, 95, 78, 144, 27, 151, 20, 252, 183, 217, 194 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 15,
                column: "RowVersion",
                value: new byte[] { 195, 247, 229, 178, 21, 125, 226, 78, 155, 241, 12, 104, 75, 39, 220, 233 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 16,
                column: "RowVersion",
                value: new byte[] { 188, 242, 150, 141, 212, 18, 244, 67, 141, 107, 43, 176, 121, 72, 243, 217 });

            migrationBuilder.UpdateData(
                table: "TrainingTypes",
                keyColumn: "Id",
                keyValue: new Guid("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b"),
                column: "RowVersion",
                value: new byte[] { 188, 145, 243, 210, 164, 100, 54, 76, 146, 24, 26, 59, 169, 189, 234, 249 });

            migrationBuilder.UpdateData(
                table: "TrainingTypes",
                keyColumn: "Id",
                keyValue: new Guid("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91"),
                column: "RowVersion",
                value: new byte[] { 117, 111, 155, 77, 150, 141, 212, 71, 157, 65, 159, 79, 74, 14, 166, 121 });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMedia_ActivityId_UploadedAtUtc",
                table: "ActivityMedia",
                columns: new[] { "ActivityId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityTypes_IsActive",
                table: "ActivityTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityTypes_Name",
                table: "ActivityTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiscActivities_ActivityTypeId",
                table: "MiscActivities",
                column: "ActivityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MiscActivities_OccurrenceDate",
                table: "MiscActivities",
                column: "OccurrenceDate");

            migrationBuilder.CreateIndex(
                name: "UX_MiscActivities_OccurrenceDate_Nomenclature",
                table: "MiscActivities",
                columns: new[] { "OccurrenceDate", "Nomenclature" },
                unique: true,
                filter: "\"DeletedUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityMedia");

            migrationBuilder.DropTable(
                name: "MiscActivities");

            migrationBuilder.DropTable(
                name: "ActivityTypes");

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 1,
                column: "RowVersion",
                value: new byte[] { 95, 111, 159, 61, 34, 255, 74, 143, 158, 242, 124, 179, 241, 169, 5, 19 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 2,
                column: "RowVersion",
                value: new byte[] { 214, 205, 160, 166, 106, 86, 75, 52, 157, 190, 166, 144, 181, 140, 219, 223 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 3,
                column: "RowVersion",
                value: new byte[] { 86, 101, 141, 138, 29, 59, 77, 154, 191, 53, 248, 240, 167, 241, 218, 107 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 4,
                column: "RowVersion",
                value: new byte[] { 167, 155, 39, 52, 55, 206, 74, 160, 143, 167, 30, 67, 213, 198, 166, 244 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 5,
                column: "RowVersion",
                value: new byte[] { 30, 252, 117, 122, 4, 252, 78, 34, 134, 229, 44, 201, 190, 58, 129, 215 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 6,
                column: "RowVersion",
                value: new byte[] { 168, 246, 169, 199, 95, 209, 76, 83, 142, 42, 194, 185, 241, 176, 240, 215 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 7,
                column: "RowVersion",
                value: new byte[] { 45, 159, 18, 52, 51, 142, 77, 74, 191, 103, 58, 193, 48, 150, 164, 184 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 8,
                column: "RowVersion",
                value: new byte[] { 79, 139, 41, 28, 47, 38, 75, 105, 155, 111, 67, 59, 33, 22, 177, 217 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 9,
                column: "RowVersion",
                value: new byte[] { 201, 192, 33, 186, 145, 174, 66, 193, 185, 232, 147, 14, 16, 183, 196, 126 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 10,
                column: "RowVersion",
                value: new byte[] { 245, 215, 166, 120, 210, 236, 77, 75, 164, 229, 44, 77, 86, 243, 241, 180 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 11,
                column: "RowVersion",
                value: new byte[] { 166, 22, 137, 176, 68, 168, 71, 64, 148, 81, 162, 169, 99, 159, 77, 157 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 12,
                column: "RowVersion",
                value: new byte[] { 227, 194, 181, 217, 18, 175, 71, 172, 182, 159, 104, 232, 230, 245, 211, 193 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 13,
                column: "RowVersion",
                value: new byte[] { 10, 125, 47, 94, 187, 180, 75, 216, 183, 61, 148, 165, 8, 42, 77, 12 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 14,
                column: "RowVersion",
                value: new byte[] { 154, 159, 76, 142, 10, 18, 78, 95, 144, 27, 151, 20, 252, 183, 217, 194 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 15,
                column: "RowVersion",
                value: new byte[] { 178, 229, 247, 195, 125, 21, 78, 226, 155, 241, 12, 104, 75, 39, 220, 233 });

            migrationBuilder.UpdateData(
                table: "TrainingRankCategoryMap",
                keyColumn: "Id",
                keyValue: 16,
                column: "RowVersion",
                value: new byte[] { 141, 150, 242, 188, 18, 212, 67, 244, 141, 107, 43, 176, 121, 72, 243, 217 });

            migrationBuilder.UpdateData(
                table: "TrainingTypes",
                keyColumn: "Id",
                keyValue: new Guid("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b"),
                column: "RowVersion",
                value: new byte[] { 210, 243, 145, 188, 100, 164, 76, 54, 146, 24, 26, 59, 169, 189, 234, 249 });

            migrationBuilder.UpdateData(
                table: "TrainingTypes",
                keyColumn: "Id",
                keyValue: new Guid("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91"),
                column: "RowVersion",
                value: new byte[] { 77, 155, 111, 117, 141, 150, 71, 212, 157, 65, 159, 79, 74, 14, 166, 121 });
        }
    }
}
