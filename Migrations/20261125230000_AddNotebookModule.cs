using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125230000_AddNotebookModule")]
    public partial class AddNotebookModule : Migration
    {
        // SECTION: Create My Notebook tables
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable("NotebookItems", table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<string>(type: "text", nullable: false),
                Title = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                BodyMarkdown = table.Column<string>(type: "text", nullable: true),
                Type = table.Column<byte>(type: "smallint", nullable: false),
                Status = table.Column<byte>(type: "smallint", nullable: false),
                Priority = table.Column<byte>(type: "smallint", nullable: false),
                ReminderAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                ColorKey = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                LegacyTodoItemId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            }, constraints: table => { table.PrimaryKey("PK_NotebookItems", x => x.Id); table.ForeignKey("FK_NotebookItems_AspNetUsers_OwnerId", x => x.OwnerId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable("NotebookTags", table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OwnerId = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_NotebookTags", x => x.Id); table.ForeignKey("FK_NotebookTags_AspNetUsers_OwnerId", x => x.OwnerId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable("NotebookAttachments", table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                NotebookItemId = table.Column<Guid>(type: "uuid", nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                UploadedById = table.Column<string>(type: "text", nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_NotebookAttachments", x => x.Id); table.ForeignKey("FK_NotebookAttachments_AspNetUsers_UploadedById", x => x.UploadedById, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_NotebookAttachments_NotebookItems_NotebookItemId", x => x.NotebookItemId, "NotebookItems", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable("NotebookChecklistItems", table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                NotebookItemId = table.Column<Guid>(type: "uuid", nullable: false),
                Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IsDone = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            }, constraints: table => { table.PrimaryKey("PK_NotebookChecklistItems", x => x.Id); table.ForeignKey("FK_NotebookChecklistItems_NotebookItems_NotebookItemId", x => x.NotebookItemId, "NotebookItems", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateTable("NotebookItemTags", table => new
            {
                NotebookItemId = table.Column<Guid>(type: "uuid", nullable: false),
                NotebookTagId = table.Column<int>(type: "integer", nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_NotebookItemTags", x => new { x.NotebookItemId, x.NotebookTagId }); table.ForeignKey("FK_NotebookItemTags_NotebookItems_NotebookItemId", x => x.NotebookItemId, "NotebookItems", "Id", onDelete: ReferentialAction.Cascade); table.ForeignKey("FK_NotebookItemTags_NotebookTags_NotebookTagId", x => x.NotebookTagId, "NotebookTags", "Id", onDelete: ReferentialAction.Cascade); });

            migrationBuilder.CreateIndex("IX_NotebookAttachments_NotebookItemId", "NotebookAttachments", "NotebookItemId");
            migrationBuilder.CreateIndex("IX_NotebookAttachments_UploadedById", "NotebookAttachments", "UploadedById");
            migrationBuilder.CreateIndex("IX_NotebookChecklistItems_NotebookItemId_SortOrder", "NotebookChecklistItems", new[] { "NotebookItemId", "SortOrder" });
            migrationBuilder.CreateIndex("IX_NotebookItems_DeletedAtUtc", "NotebookItems", "DeletedAtUtc");
            migrationBuilder.CreateIndex("IX_NotebookItems_OwnerId_IsPinned_UpdatedAtUtc", "NotebookItems", new[] { "OwnerId", "IsPinned", "UpdatedAtUtc" });
            migrationBuilder.CreateIndex("IX_NotebookItems_OwnerId_LegacyTodoItemId", "NotebookItems", new[] { "OwnerId", "LegacyTodoItemId" });
            migrationBuilder.CreateIndex("IX_NotebookItems_OwnerId_ReminderAtUtc", "NotebookItems", new[] { "OwnerId", "ReminderAtUtc" });
            migrationBuilder.CreateIndex("IX_NotebookItems_OwnerId_Status_Type", "NotebookItems", new[] { "OwnerId", "Status", "Type" });
            migrationBuilder.CreateIndex("IX_NotebookItemTags_NotebookTagId", "NotebookItemTags", "NotebookTagId");
            migrationBuilder.CreateIndex("IX_NotebookTags_OwnerId_NormalizedName", "NotebookTags", new[] { "OwnerId", "NormalizedName" }, unique: true);
        }

        // SECTION: Remove My Notebook tables
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("NotebookAttachments");
            migrationBuilder.DropTable("NotebookChecklistItems");
            migrationBuilder.DropTable("NotebookItemTags");
            migrationBuilder.DropTable("NotebookItems");
            migrationBuilder.DropTable("NotebookTags");
        }
    }
}
