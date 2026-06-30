using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201100000_AddPrismMediaOutbox")]
public partial class AddPrismMediaOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PrismMediaOutboxMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ActivityId = table.Column<int>(type: "integer", nullable: true),
                AttachmentId = table.Column<int>(type: "integer", nullable: true),
                StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AvailableAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessingStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LockExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PrismMediaOutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PrismMediaOutboxMessages_ActivityId_Status",
            table: "PrismMediaOutboxMessages",
            columns: new[] { "ActivityId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PrismMediaOutboxMessages_AttachmentId_Status",
            table: "PrismMediaOutboxMessages",
            columns: new[] { "AttachmentId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PrismMediaOutboxMessages_EventId",
            table: "PrismMediaOutboxMessages",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PrismMediaOutboxMessages_LockExpiresAtUtc",
            table: "PrismMediaOutboxMessages",
            column: "LockExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_PrismMediaOutboxMessages_Queue",
            table: "PrismMediaOutboxMessages",
            columns: new[] { "Status", "AvailableAfterUtc", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PrismMediaOutboxMessages");
    }
}
