using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEventCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE Events SET Category = 2 WHERE Category IN (0, 2);
UPDATE Events SET Category = 3 WHERE Category IN (1, 3, 4);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE Events SET Category = 0 WHERE Category = 2;
UPDATE Events SET Category = 4 WHERE Category = 3;
""");
        }
    }
}
