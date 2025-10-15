using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSocialMediaReach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reach",
                table: "SocialMediaEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.AddColumn<int>(
                    name: "Reach",
                    table: "SocialMediaEvents",
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);
            }
            else
            {
                migrationBuilder.AddColumn<int>(
                    name: "Reach",
                    table: "SocialMediaEvents",
                    type: "int",
                    nullable: false,
                    defaultValue: 0);
            }
        }
    }
}
