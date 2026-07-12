using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201110000_HardenNotifications")]
public partial class HardenNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredUtc",
            table: "Notifications",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "Notifications",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeadLetteredUtc",
            table: "NotificationDispatches",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LockToken",
            table: "NotificationDispatches",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // Existing rows pre-date separate occurrence/delivery timestamps. Treat their original
        // creation timestamp as the best available delivery time.
        migrationBuilder.Sql(
            "UPDATE \"Notifications\" SET \"DeliveredUtc\" = \"CreatedUtc\" WHERE \"DeliveredUtc\" IS NULL;");

        // Existing mute records may pre-date the invariant that muting closes the current unread
        // backlog. Reconcile those rows once so the badge, filters and read-all semantics agree
        // immediately after deployment.
        migrationBuilder.Sql(
            """
            UPDATE "Notifications" AS notification
            SET
                "SeenUtc" = COALESCE(notification."SeenUtc", notification."CreatedUtc"),
                "ReadUtc" = COALESCE(notification."ReadUtc", notification."CreatedUtc")
            FROM "UserProjectMutes" AS mute
            WHERE notification."RecipientUserId" = mute."UserId"
              AND notification."ProjectId" = mute."ProjectId"
              AND notification."ReadUtc" IS NULL;
            """);

        migrationBuilder.DropIndex(
            name: "IX_Notifications_Fingerprint",
            table: "Notifications");

        migrationBuilder.DropIndex(
            name: "IX_Notifications_RecipientUserId_CreatedUtc",
            table: "Notifications");

        migrationBuilder.DropIndex(
            name: "IX_Notifications_SourceDispatchId",
            table: "Notifications");

        // Remove legacy duplicates before enforcing recipient-scoped idempotency. The newest row
        // is retained because it has the most recent read/seen state and route metadata.
        migrationBuilder.Sql(
            """
            DELETE FROM "Notifications"
            WHERE "Id" IN
            (
                SELECT "Id"
                FROM
                (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY "RecipientUserId", "Fingerprint"
                            ORDER BY "Id" DESC
                        ) AS "DuplicateOrdinal"
                    FROM "Notifications"
                    WHERE "Fingerprint" IS NOT NULL
                ) AS duplicates
                WHERE duplicates."DuplicateOrdinal" > 1
            );
            """);

        // A dispatch is the durable identity of one recipient notification. If historical data
        // contains duplicate links, retain the newest link and preserve the older notification by
        // clearing only its optional dispatch reference.
        migrationBuilder.Sql(
            """
            UPDATE "Notifications"
            SET "SourceDispatchId" = NULL
            WHERE "Id" IN
            (
                SELECT "Id"
                FROM
                (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY "SourceDispatchId"
                            ORDER BY "Id" DESC
                        ) AS "DuplicateOrdinal"
                    FROM "Notifications"
                    WHERE "SourceDispatchId" IS NOT NULL
                ) AS duplicates
                WHERE duplicates."DuplicateOrdinal" > 1
            );
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_RecipientUserId_CreatedUtc_Id",
            table: "Notifications",
            columns: new[] { "RecipientUserId", "CreatedUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_SourceDispatchId",
            table: "Notifications",
            column: "SourceDispatchId",
            unique: true,
            filter: "\"SourceDispatchId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_RecipientUserId_Fingerprint",
            table: "Notifications",
            columns: new[] { "RecipientUserId", "Fingerprint" },
            unique: true,
            filter: "\"Fingerprint\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_NotificationDispatches_DispatchedUtc_DeadLetteredUtc_LockedUntilUtc",
            table: "NotificationDispatches",
            columns: new[] { "DispatchedUtc", "DeadLetteredUtc", "LockedUntilUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_NotificationDispatches_LockToken",
            table: "NotificationDispatches",
            column: "LockToken");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NotificationDispatches_DispatchedUtc_DeadLetteredUtc_LockedUntilUtc",
            table: "NotificationDispatches");

        migrationBuilder.DropIndex(
            name: "IX_NotificationDispatches_LockToken",
            table: "NotificationDispatches");

        migrationBuilder.DropIndex(
            name: "IX_Notifications_RecipientUserId_CreatedUtc_Id",
            table: "Notifications");

        migrationBuilder.DropIndex(
            name: "IX_Notifications_RecipientUserId_Fingerprint",
            table: "Notifications");

        migrationBuilder.DropIndex(
            name: "IX_Notifications_SourceDispatchId",
            table: "Notifications");

        migrationBuilder.DropColumn(
            name: "DeliveredUtc",
            table: "Notifications");

        migrationBuilder.DropColumn(
            name: "Kind",
            table: "Notifications");

        migrationBuilder.DropColumn(
            name: "DeadLetteredUtc",
            table: "NotificationDispatches");

        migrationBuilder.DropColumn(
            name: "LockToken",
            table: "NotificationDispatches");

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_Fingerprint",
            table: "Notifications",
            column: "Fingerprint",
            filter: "\"Fingerprint\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_RecipientUserId_CreatedUtc",
            table: "Notifications",
            columns: new[] { "RecipientUserId", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_SourceDispatchId",
            table: "Notifications",
            column: "SourceDispatchId");
    }
}
