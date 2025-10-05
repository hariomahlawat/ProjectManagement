using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class ExpandNotificationDispatchMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorUserId",
                table: "NotificationDispatches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "NotificationDispatches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "NotificationDispatches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "NotificationDispatches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "NotificationDispatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Route",
                table: "NotificationDispatches",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeId",
                table: "NotificationDispatches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "NotificationDispatches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "NotificationDispatches",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "NotificationDispatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ActorUserId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ActorUserId", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_Fingerprint",
                table: "NotificationDispatches",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_Module_EventType_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "Module", "EventType", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ProjectId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ProjectId", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ScopeType_ScopeId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ScopeType", "ScopeId", "DispatchedUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationDispatches_ActorUserId_DispatchedUtc",
                table: "NotificationDispatches");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDispatches_Fingerprint",
                table: "NotificationDispatches");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDispatches_Module_EventType_DispatchedUtc",
                table: "NotificationDispatches");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDispatches_ProjectId_DispatchedUtc",
                table: "NotificationDispatches");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDispatches_ScopeType_ScopeId_DispatchedUtc",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "Route",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "NotificationDispatches");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "NotificationDispatches");
        }
    }
}
