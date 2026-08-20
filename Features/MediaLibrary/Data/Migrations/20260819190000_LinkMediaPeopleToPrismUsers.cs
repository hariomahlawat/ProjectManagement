using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260819190000_LinkMediaPeopleToPrismUsers")]
public sealed class LinkMediaPeopleToPrismUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "MediaPersonUserLinks" (
    "Id" uuid NOT NULL,
    "MediaPersonId" uuid NOT NULL,
    "UserId" varchar(450) NOT NULL,
    "LinkedByUserId" varchar(450) NOT NULL,
    "LinkedAtUtc" timestamptz NOT NULL,
    "UnlinkedByUserId" varchar(450) NULL,
    "UnlinkedAtUtc" timestamptz NULL,
    "UnlinkReason" varchar(1024) NULL,
    "ConcurrencyToken" uuid NOT NULL,
    CONSTRAINT "PK_MediaPersonUserLinks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MediaPersonUserLinks_MediaPersons_MediaPersonId"
        FOREIGN KEY ("MediaPersonId") REFERENCES "MediaPersons"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_MediaPersonUserLinks_ActivePerson"
ON "MediaPersonUserLinks" ("MediaPersonId")
WHERE "UnlinkedAtUtc" IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_MediaPersonUserLinks_ActiveUser"
ON "MediaPersonUserLinks" ("UserId")
WHERE "UnlinkedAtUtc" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_MediaPersonUserLinks_UserHistory"
ON "MediaPersonUserLinks" ("UserId", "LinkedAtUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"MediaPersonUserLinks\";");
    }
}
