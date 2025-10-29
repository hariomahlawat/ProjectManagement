using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class ffc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "Name", "RowVersion" },
                values: new object[] { "All types of administrative tasks/ events or activities.", "Adm Activities", new byte[] { 216, 208, 195, 169, 249, 15, 192, 73, 139, 118, 95, 193, 29, 92, 16, 222 } });

            migrationBuilder.UpdateData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "Name", "RowVersion" },
                values: new object[] { "All internal and external inspections.", "Inspections", new byte[] { 141, 95, 207, 27, 182, 148, 187, 79, 137, 223, 209, 223, 15, 158, 156, 66 } });

            migrationBuilder.InsertData(
                table: "ActivityTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { 3, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Engagements with academic institutions and partners.", true, null, null, "Academia Interaction", new byte[] { 122, 28, 155, 220, 131, 79, 23, 77, 142, 55, 159, 210, 102, 90, 30, 59 } },
                    { 4, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Collaboration with industry stakeholders and forums.", true, null, null, "Industry Interaction", new byte[] { 248, 153, 190, 112, 55, 17, 127, 78, 158, 99, 47, 8, 107, 92, 104, 77 } },
                    { 5, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Educational seminars, lectures, and talks.", true, null, null, "Seminar/ Lecture", new byte[] { 146, 250, 117, 199, 45, 67, 185, 73, 143, 98, 111, 142, 234, 12, 126, 155 } },
                    { 6, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Activities that do not fit other defined categories.", true, null, null, "Misc", new byte[] { 140, 13, 2, 92, 75, 122, 79, 79, 136, 34, 159, 108, 242, 8, 136, 127 } }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.UpdateData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "Name", "RowVersion" },
                values: new object[] { "Formal training activities.", "Training", new byte[] { 108, 91, 159, 47, 254, 11, 35, 79, 157, 60, 15, 210, 42, 111, 106, 75 } });

            migrationBuilder.UpdateData(
                table: "ActivityTypes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "Name", "RowVersion" },
                values: new object[] { "Stakeholder engagement or outreach.", "Engagement", new byte[] { 167, 196, 177, 201, 58, 46, 55, 74, 155, 10, 91, 232, 95, 78, 217, 87 } });
        }
    }
}
