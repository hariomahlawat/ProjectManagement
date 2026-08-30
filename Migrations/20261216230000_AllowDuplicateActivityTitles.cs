using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Activity titles are descriptive labels rather than identifiers. Recurring reviews,
/// meetings and interactions can legitimately reuse the same title and activity type.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216230000_AllowDuplicateActivityTitles")]
public sealed class AllowDuplicateActivityTitles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Activities_ActivityTypeId_Title",
            table: "Activities");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Restoring the historical filtered Activity title index is supported only on PostgreSQL.");
        }

        migrationBuilder.CreateIndex(
            name: "UX_Activities_ActivityTypeId_Title",
            table: "Activities",
            columns: new[] { "ActivityTypeId", "Title" },
            unique: true,
            filter: "\"IsDeleted\" = FALSE");
    }
}
