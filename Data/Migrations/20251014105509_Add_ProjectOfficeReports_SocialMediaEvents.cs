using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_ProjectOfficeReports_SocialMediaEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Visits_VisitPhotos_CoverPhotoId",
                table: "Visits");

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.CreateTable(
                    name: "SocialMediaEventTypes",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                        CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEventTypes", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SocialMediaEventPhotos",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        SocialMediaEventId = table.Column<Guid>(type: "uuid", nullable: false),
                        StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                        StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                        ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Width = table.Column<int>(type: "integer", nullable: false),
                        Height = table.Column<int>(type: "integer", nullable: false),
                        Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        VersionStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        IsCover = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                        CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                        LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEventPhotos", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SocialMediaEvents",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        SocialMediaEventTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                        DateOfEvent = table.Column<DateOnly>(type: "date", nullable: false),
                        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        Platform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        Reach = table.Column<int>(type: "integer", nullable: false),
                        Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                        CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                        CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                        LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEvents", x => x.Id);
                        table.ForeignKey(
                            name: "FK_SocialMediaEvents_SocialMediaEventPhotos_CoverPhotoId",
                            column: x => x.CoverPhotoId,
                            principalTable: "SocialMediaEventPhotos",
                            principalColumn: "Id");
                        table.ForeignKey(
                            name: "FK_SocialMediaEvents_SocialMediaEventTypes_SocialMediaEventTypeId",
                            column: x => x.SocialMediaEventTypeId,
                            principalTable: "SocialMediaEventTypes",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEventPhotos_EventId_CreatedAtUtc",
                    table: "SocialMediaEventPhotos",
                    columns: new[] { "SocialMediaEventId", "CreatedAtUtc" });

                migrationBuilder.CreateIndex(
                    name: "UX_SocialMediaEventPhotos_IsCover",
                    table: "SocialMediaEventPhotos",
                    columns: new[] { "SocialMediaEventId", "IsCover" },
                    unique: true,
                    filter: "\"IsCover\" = TRUE");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_CoverPhotoId",
                    table: "SocialMediaEvents",
                    column: "CoverPhotoId");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_DateOfEvent",
                    table: "SocialMediaEvents",
                    column: "DateOfEvent");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_SocialMediaEventTypeId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaEventTypeId");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEventTypes_Name",
                    table: "SocialMediaEventTypes",
                    column: "Name",
                    unique: true);
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "SocialMediaEventTypes",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                        Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                        IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                        CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                        LastModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        RowVersion = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEventTypes", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SocialMediaEventPhotos",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        SocialMediaEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        StorageKey = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                        StoragePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, defaultValue: ""),
                        ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                        Width = table.Column<int>(type: "int", nullable: false),
                        Height = table.Column<int>(type: "int", nullable: false),
                        Caption = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                        VersionStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                        IsCover = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                        CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                        LastModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                        RowVersion = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEventPhotos", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "SocialMediaEvents",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        SocialMediaEventTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                        DateOfEvent = table.Column<DateOnly>(type: "date", nullable: false),
                        Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                        Platform = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                        Reach = table.Column<int>(type: "int", nullable: false),
                        Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                        CoverPhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                        CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                        CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                        LastModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                        LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                        RowVersion = table.Column<byte[]>(type: "varbinary(16)", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SocialMediaEvents", x => x.Id);
                        table.ForeignKey(
                            name: "FK_SocialMediaEvents_SocialMediaEventPhotos_CoverPhotoId",
                            column: x => x.CoverPhotoId,
                            principalTable: "SocialMediaEventPhotos",
                            principalColumn: "Id");
                        table.ForeignKey(
                            name: "FK_SocialMediaEvents_SocialMediaEventTypes_SocialMediaEventTypeId",
                            column: x => x.SocialMediaEventTypeId,
                            principalTable: "SocialMediaEventTypes",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEventPhotos_EventId_CreatedAtUtc",
                    table: "SocialMediaEventPhotos",
                    columns: new[] { "SocialMediaEventId", "CreatedAtUtc" });

                migrationBuilder.CreateIndex(
                    name: "UX_SocialMediaEventPhotos_IsCover",
                    table: "SocialMediaEventPhotos",
                    columns: new[] { "SocialMediaEventId", "IsCover" },
                    unique: true,
                    filter: "[IsCover] = 1");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_CoverPhotoId",
                    table: "SocialMediaEvents",
                    column: "CoverPhotoId");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_DateOfEvent",
                    table: "SocialMediaEvents",
                    column: "DateOfEvent");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEvents_SocialMediaEventTypeId",
                    table: "SocialMediaEvents",
                    column: "SocialMediaEventTypeId");

                migrationBuilder.CreateIndex(
                    name: "IX_SocialMediaEventTypes_Name",
                    table: "SocialMediaEventTypes",
                    column: "Name",
                    unique: true);
            }

            migrationBuilder.InsertData(
                table: "SocialMediaEventTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { new Guid("0b35f77a-4ef6-4a0a-85f9-9fa0b1b0c353"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Stories focused on community outreach and engagement.", true, null, null, "Community Engagement", new byte[] { 156, 101, 26, 107, 203, 244, 144, 76, 138, 54, 143, 246, 185, 53, 94, 125 } },
                    { new Guid("9ddf8646-7070-4f7a-9fa0-8cb19f4a0d5b"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Coverage for new campaign announcements and kick-off posts.", true, null, null, "Campaign Launch", new byte[] { 245, 68, 156, 111, 242, 153, 246, 71, 148, 115, 208, 182, 163, 36, 184, 37 } },
                    { new Guid("fa2f60fa-7d4f-4f60-a84b-e8f64dce0b73"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Highlights of major delivery milestones shared online.", true, null, null, "Milestone Update", new byte[] { 40, 230, 184, 11, 72, 157, 218, 71, 147, 5, 43, 110, 111, 141, 165, 198 } }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_VisitPhotos_CoverPhotoId",
                table: "Visits",
                column: "CoverPhotoId",
                principalTable: "VisitPhotos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId",
                table: "SocialMediaEventPhotos",
                column: "SocialMediaEventId",
                principalTable: "SocialMediaEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Visits_VisitPhotos_CoverPhotoId",
                table: "Visits");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId",
                table: "SocialMediaEventPhotos");

            migrationBuilder.DropTable(
                name: "SocialMediaEvents");

            migrationBuilder.DropTable(
                name: "SocialMediaEventPhotos");

            migrationBuilder.DropTable(
                name: "SocialMediaEventTypes");

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_VisitPhotos_CoverPhotoId",
                table: "Visits",
                column: "CoverPhotoId",
                principalTable: "VisitPhotos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
