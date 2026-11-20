using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <inheritdoc />
    public partial class AltKategoriler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubcategoryId",
                table: "Notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Subcategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subcategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subcategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_SubcategoryId",
                table: "Notes",
                column: "SubcategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Subcategories_CategoryId_Name",
                table: "Subcategories",
                columns: new[] { "CategoryId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Subcategories_SubcategoryId",
                table: "Notes",
                column: "SubcategoryId",
                principalTable: "Subcategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Subcategories_SubcategoryId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "Subcategories");

            migrationBuilder.DropIndex(
                name: "IX_Notes_SubcategoryId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "SubcategoryId",
                table: "Notes");
        }
    }
}
