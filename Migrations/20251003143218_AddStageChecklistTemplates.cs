using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddStageChecklistTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "NotificationDispatches",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "StageChecklistTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistTemplates_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StageChecklistItemTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistItemTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistItemTemplates_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StageChecklistItemTemplates_StageChecklistTemplates_Templat~",
                        column: x => x.TemplateId,
                        principalTable: "StageChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageChecklistAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PerformedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistAudits_StageChecklistItemTemplates_ItemId",
                        column: x => x.ItemId,
                        principalTable: "StageChecklistItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StageChecklistAudits_StageChecklistTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "StageChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistAudits_ItemId",
                table: "StageChecklistAudits",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistAudits_TemplateId",
                table: "StageChecklistAudits",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistItemTemplates_TemplateId_Sequence",
                table: "StageChecklistItemTemplates",
                columns: new[] { "TemplateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistItemTemplates_UpdatedByUserId",
                table: "StageChecklistItemTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistTemplates_UpdatedByUserId",
                table: "StageChecklistTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistTemplates_Version_StageCode",
                table: "StageChecklistTemplates",
                columns: new[] { "Version", "StageCode" },
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO ""StageChecklistTemplates"" (""Version"", ""StageCode"", ""UpdatedOn"", ""RowVersion"")
                SELECT DISTINCT st.""Version"", st.""Code"", now() at time zone 'utc',
                       decode(md5(random()::text || clock_timestamp()::text), 'hex')
                FROM ""StageTemplates"" st
                ON CONFLICT (""Version"", ""StageCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageChecklistAudits");

            migrationBuilder.DropTable(
                name: "StageChecklistItemTemplates");

            migrationBuilder.DropTable(
                name: "StageChecklistTemplates");

            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "NotificationDispatches",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);
        }
    }
}
