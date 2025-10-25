using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrainingTrainees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArmyNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Rank = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UnitName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<byte>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingTrainees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingTrainees_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTrainees_TrainingId",
                table: "TrainingTrainees",
                column: "TrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTrainees_TrainingId_ArmyNumber",
                table: "TrainingTrainees",
                columns: new[] { "TrainingId", "ArmyNumber" },
                unique: true,
                filter: "\"ArmyNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingTrainees");
        }
    }
}
