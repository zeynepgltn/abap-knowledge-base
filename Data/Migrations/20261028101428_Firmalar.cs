using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <inheritdoc />
    public partial class Firmalar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Notes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_CompanyId",
                table: "Notes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Companies_CompanyId",
                table: "Notes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Veri: "Proje Araştırmaları" -> "Projeler"; OneNote'tan gelen proje notlarının bölüm adı firma olur
            // ("OneNote: projeler › Firma A" -> firma "Firma A"). Boş veritabanında etkisizdir.
            migrationBuilder.Sql("""
                UPDATE Categories SET Name = N'Projeler'
                WHERE Name = N'Proje Araştırmaları' AND NOT EXISTS (SELECT 1 FROM Categories WHERE Name = N'Projeler');

                INSERT INTO Companies (Name)
                SELECT DISTINCT LEFT(LTRIM(RTRIM(SUBSTRING(n.SourceFile, CHARINDEX(N'›', n.SourceFile) + 1, 400))), 100)
                FROM Notes n JOIN Categories c ON c.Id = n.CategoryId
                WHERE c.Name = N'Projeler' AND n.SourceFile LIKE N'OneNote: %›%';

                UPDATE n SET CompanyId = co.Id
                FROM Notes n
                JOIN Categories c ON c.Id = n.CategoryId
                JOIN Companies co ON co.Name = LEFT(LTRIM(RTRIM(SUBSTRING(n.SourceFile, CHARINDEX(N'›', n.SourceFile) + 1, 400))), 100)
                WHERE c.Name = N'Projeler' AND n.SourceFile LIKE N'OneNote: %›%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Companies_CompanyId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Notes_CompanyId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Notes");
        }
    }
}
