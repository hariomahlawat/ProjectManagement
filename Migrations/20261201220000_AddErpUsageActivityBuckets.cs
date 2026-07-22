using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201220000_AddErpUsageActivityBuckets")]
public partial class AddErpUsageActivityBuckets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserActivityBuckets",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                BucketStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActivityDateIst = table.Column<DateOnly>(type: "date", nullable: false),
                ModuleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HadNavigation = table.Column<bool>(type: "boolean", nullable: false),
                HadInteractiveHeartbeat = table.Column<bool>(type: "boolean", nullable: false),
                FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                NavigationCount = table.Column<int>(type: "integer", nullable: false),
                HeartbeatCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserActivityBuckets", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserActivityBuckets_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityBuckets_ActivityDateIst_UserId",
            table: "UserActivityBuckets",
            columns: new[] { "ActivityDateIst", "UserId" });

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityBuckets_ModuleKey_ActivityDateIst",
            table: "UserActivityBuckets",
            columns: new[] { "ModuleKey", "ActivityDateIst" });

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityBuckets_UserId_BucketStartUtc_ModuleKey",
            table: "UserActivityBuckets",
            columns: new[] { "UserId", "BucketStartUtc", "ModuleKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserActivityBuckets");
    }
}
