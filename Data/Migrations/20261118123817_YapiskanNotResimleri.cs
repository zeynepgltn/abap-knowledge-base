using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <inheritdoc />
    public partial class YapiskanNotResimleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StickyImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StickyNoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StickyImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StickyImages_StickyNotes_StickyNoteId",
                        column: x => x.StickyNoteId,
                        principalTable: "StickyNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StickyImages_StickyNoteId",
                table: "StickyImages",
                column: "StickyNoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StickyImages");
        }
    }
}
