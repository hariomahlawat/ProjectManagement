using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// When a chat attachment was last restored after a removal. Automatic retention leaves a
    /// restored file alone for a grace period, so undoing a retention clean-up is not reversed
    /// by the next daily run. Additive: a nullable column, null for every existing row.
    /// </remarks>
    public partial class AddChatAttachmentRestoredUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RestoredUtc",
                table: "ChatAttachments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestoredUtc",
                table: "ChatAttachments");
        }
    }
}
