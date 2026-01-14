using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125090000_HardenIndustryPartners")]
    public partial class HardenIndustryPartners : Migration
    {
        // SECTION: Apply industry partner hardening changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "IndustryPartners",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.Sql("""
                UPDATE "IndustryPartnerProjectAssociations"
                SET "Role" = 'DevelopmentPartner'
                WHERE "Role" = 'Development Partner';
            """);

            migrationBuilder.Sql("""
                UPDATE "IndustryPartnerProjectAssociations"
                SET "Role" = 'ToTRecipient'
                WHERE "Role" = 'ToT Recipient';
            """);

            migrationBuilder.Sql("""
                UPDATE "IndustryPartners"
                SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text), 'hex')
                WHERE octet_length("RowVersion") = 0;
            """);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "IndustryPartners",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_IsActive",
                table: "IndustryPartnerProjectAssociations",
                columns: new[] { "IndustryPartnerId", "IsActive" });
        }

        // SECTION: Revert industry partner hardening changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_IsActive",
                table: "IndustryPartnerProjectAssociations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "IndustryPartners");

            migrationBuilder.Sql("""
                UPDATE "IndustryPartnerProjectAssociations"
                SET "Role" = 'Development Partner'
                WHERE "Role" = 'DevelopmentPartner';
            """);

            migrationBuilder.Sql("""
                UPDATE "IndustryPartnerProjectAssociations"
                SET "Role" = 'ToT Recipient'
                WHERE "Role" = 'ToTRecipient';
            """);
        }
    }
}
