using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class ExpandNotificationPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AlterColumn<string>(
                    name: "PayloadJson",
                    table: "NotificationDispatches",
                    type: "nvarchar(max)",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(4000)",
                    oldMaxLength: 4000);
            }
            else
            {
                migrationBuilder.AlterColumn<string>(
                    name: "PayloadJson",
                    table: "NotificationDispatches",
                    type: "text",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(4000)",
                    oldMaxLength: 4000);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AlterColumn<string>(
                    name: "PayloadJson",
                    table: "NotificationDispatches",
                    type: "nvarchar(4000)",
                    maxLength: 4000,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(max)");
            }
            else
            {
                migrationBuilder.AlterColumn<string>(
                    name: "PayloadJson",
                    table: "NotificationDispatches",
                    type: "character varying(4000)",
                    maxLength: 4000,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "text");
            }
        }
    }
}
