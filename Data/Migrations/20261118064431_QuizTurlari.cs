using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuizTurlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizDays_Date",
                table: "QuizDays");

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "QuizDays",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_QuizDays_Date_Round",
                table: "QuizDays",
                columns: new[] { "Date", "Round" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizDays_Date_Round",
                table: "QuizDays");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "QuizDays");

            migrationBuilder.CreateIndex(
                name: "IX_QuizDays_Date",
                table: "QuizDays",
                column: "Date",
                unique: true);
        }
    }
}
