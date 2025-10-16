using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20251016020000_TotTracker")]
public partial class TotTracker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Schema consolidated into 20251016005917_InitialMigrations; this migration is intentionally a no-op
        // to keep history aligned for databases that have already applied the earlier standalone script.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
