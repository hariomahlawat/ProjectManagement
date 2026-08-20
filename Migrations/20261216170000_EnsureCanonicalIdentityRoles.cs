using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216170000_EnsureCanonicalIdentityRoles")]
public partial class EnsureCanonicalIdentityRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Production does not rely on startup seeders. Keep the institutional
        // assignable-role catalogue available through the normal migration path.
        // Existing rows win by normalized role name; only genuinely missing roles
        // are inserted. This is intentionally idempotent.
        migrationBuilder.Sql("""
            INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            SELECT seed."Id", seed."Name", seed."NormalizedName", seed."ConcurrencyStamp"
            FROM (VALUES
                ('40746e33-a5d5-52e3-8644-f1b96a005c7a', 'Admin', 'ADMIN', 'prism-role-admin-v1'),
                ('f4f0c56e-dfc7-56e0-a2ee-3dc71c8f913a', 'Comdt', 'COMDT', 'prism-role-comdt-v1'),
                ('61f6dd94-ee5e-5e5b-80d7-81c1df3e98ca', 'HoD', 'HOD', 'prism-role-hod-v1'),
                ('94a1bd25-b087-5442-84c9-b760774e61a5', 'Project Officer', 'PROJECT OFFICER', 'prism-role-project-officer-v1'),
                ('3e500ad0-1a0c-5b85-a185-3a2fcf554a2c', 'Project Office', 'PROJECT OFFICE', 'prism-role-project-office-v1'),
                ('5dc290ed-feb6-53d5-9ce1-81086cf8041a', 'MCO', 'MCO', 'prism-role-mco-v1'),
                ('cc9065d0-bae2-522f-9c9c-45cbdf2b0443', 'TA', 'TA', 'prism-role-ta-v1'),
                ('7f0e532f-bf53-546a-a9f2-80fa15230b87', 'ITO', 'ITO', 'prism-role-ito-v1'),
                ('0a5c9f48-6681-5506-a8cb-07e249ecf48c', 'Main_Office_Clerk', 'MAIN_OFFICE_CLERK', 'prism-role-main-office-clerk-v1'),
                ('98cd4b1b-07f7-5f7c-8ff1-02a83e97d5d0', 'MC_Cell_Clerk', 'MC_CELL_CLERK', 'prism-role-mc-cell-clerk-v1'),
                ('e39bc4d0-16df-542e-bd1c-12eac5d93d65', 'IT_Cell_Clerk', 'IT_CELL_CLERK', 'prism-role-it-cell-clerk-v1')
            ) AS seed("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            WHERE NOT EXISTS (
                SELECT 1
                FROM "AspNetRoles" existing
                WHERE UPPER(COALESCE(existing."NormalizedName", existing."Name", ''))
                      = seed."NormalizedName"
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Delete only rows created with this migration's deterministic identifiers,
        // and only while they remain completely unused. Assigned roles are retained
        // during rollback so access records are never orphaned.
        migrationBuilder.Sql("""
            DELETE FROM "AspNetRoles" AS r
            WHERE r."Id" IN (
                '40746e33-a5d5-52e3-8644-f1b96a005c7a',
                'f4f0c56e-dfc7-56e0-a2ee-3dc71c8f913a',
                '61f6dd94-ee5e-5e5b-80d7-81c1df3e98ca',
                '94a1bd25-b087-5442-84c9-b760774e61a5',
                '3e500ad0-1a0c-5b85-a185-3a2fcf554a2c',
                '5dc290ed-feb6-53d5-9ce1-81086cf8041a',
                'cc9065d0-bae2-522f-9c9c-45cbdf2b0443',
                '7f0e532f-bf53-546a-a9f2-80fa15230b87',
                '0a5c9f48-6681-5506-a8cb-07e249ecf48c',
                '98cd4b1b-07f7-5f7c-8ff1-02a83e97d5d0',
                'e39bc4d0-16df-542e-bd1c-12eac5d93d65'
            )
              AND NOT EXISTS (
                  SELECT 1 FROM "AspNetUserRoles" ur WHERE ur."RoleId" = r."Id"
              )
              AND NOT EXISTS (
                  SELECT 1 FROM "AspNetRoleClaims" rc WHERE rc."RoleId" = r."Id"
              );
            """);
    }
}
