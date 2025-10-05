using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.CreateTable(
                    name: "Notifications",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        Module = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        ScopeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        ProjectId = table.Column<int>(type: "integer", nullable: true),
                        ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        Route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                        Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                        CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                        SeenUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                        ReadUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                        SourceDispatchId = table.Column<int>(type: "integer", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Notifications", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Notifications_NotificationDispatches_SourceDispatchId",
                            column: x => x.SourceDispatchId,
                            principalTable: "NotificationDispatches",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.SetNull);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Notifications_Fingerprint",
                    table: "Notifications",
                    column: "Fingerprint",
                    filter: "\"Fingerprint\" IS NOT NULL");
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "Notifications",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "int", nullable: false)
                            .Annotation("SqlServer:Identity", "1, 1"),
                        RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        Module = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                        EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                        ScopeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                        ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                        ProjectId = table.Column<int>(type: "int", nullable: true),
                        ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                        Route = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                        Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                        Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                        CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                        SeenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                        ReadUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                        SourceDispatchId = table.Column<int>(type: "int", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Notifications", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Notifications_NotificationDispatches_SourceDispatchId",
                            column: x => x.SourceDispatchId,
                            principalTable: "NotificationDispatches",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.SetNull);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Notifications_Fingerprint",
                    table: "Notifications",
                    column: "Fingerprint",
                    filter: "[Fingerprint] IS NOT NULL");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ReadUtc_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ReadUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_SeenUtc_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "SeenUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SourceDispatchId",
                table: "Notifications",
                column: "SourceDispatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
