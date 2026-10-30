using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <inheritdoc />
    public partial class SayfaSirasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_CategoryId",
                table: "Notes");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Notes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_CategoryId_SortOrder",
                table: "Notes",
                columns: new[] { "CategoryId", "SortOrder" });

            // Başlangıç sırası: her kategoride OneNote'taki sıra (kaynak dosya/bölüm, sonra sayfa no).
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT SortOrder, ROW_NUMBER() OVER (PARTITION BY CategoryId ORDER BY SourceFile, SourcePage, Id) AS rn
                    FROM Notes)
                UPDATE ordered SET SortOrder = rn;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_CategoryId_SortOrder",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Notes");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_CategoryId",
                table: "Notes",
                column: "CategoryId");
        }
    }
}
