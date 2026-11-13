using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <summary>
    /// Notes.Tags (virgüllü metin) -> Tags + NoteTags, Notes.SourceFile (metin) -> Sources + Notes.SourceId.
    /// Var olan veriler taşınır, eski sütunlar en son silinir. Her nota kendiliğinden eklenen "onenote"
    /// etiketi taşınmaz: notun OneNote'tan geldiği artık kaynak bilgisinden görülüyor.
    /// </summary>
    public partial class EtiketKaynakTablolari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "Notes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteTags",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTags", x => new { x.NoteId, x.TagId });
                    table.ForeignKey(
                        name: "FK_NoteTags_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoteTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Kaynaklar: "OneNote: defter › bölüm" -> (OneNote, defter, bölüm); diğer metinler -> (Dosya, metin).
            migrationBuilder.Sql("""
                SELECT n.Id,
                       CASE WHEN n.SourceFile LIKE N'OneNote: %›%' THEN N'OneNote' ELSE N'Dosya' END AS Kind,
                       LEFT(LTRIM(RTRIM(CASE WHEN n.SourceFile LIKE N'OneNote: %›%'
                            THEN SUBSTRING(n.SourceFile, 10, CHARINDEX(N'›', n.SourceFile) - 10)
                            ELSE n.SourceFile END)), 200) AS Name,
                       CASE WHEN n.SourceFile LIKE N'OneNote: %›%'
                            THEN LEFT(LTRIM(RTRIM(SUBSTRING(n.SourceFile, CHARINDEX(N'›', n.SourceFile) + 1, 400))), 300) END AS Section
                INTO #src
                FROM Notes n
                WHERE n.SourceFile IS NOT NULL AND LTRIM(RTRIM(n.SourceFile)) <> N'';

                INSERT INTO Sources (Kind, Name, Section) SELECT DISTINCT Kind, Name, Section FROM #src;

                UPDATE n SET SourceId = s.Id
                FROM Notes n
                JOIN #src x ON x.Id = n.Id
                JOIN Sources s ON s.Kind = x.Kind AND s.Name = x.Name AND (s.Section = x.Section OR (s.Section IS NULL AND x.Section IS NULL));

                DROP TABLE #src;
                """);

            // Etiketler: virgülle ayrılmış metin bölünür, boşluklar kırpılır, aynı ad bir kez tutulur.
            migrationBuilder.Sql("""
                SELECT DISTINCT n.Id AS NoteId, LEFT(LTRIM(RTRIM(v.value)), 100) AS Name
                INTO #tg
                FROM Notes n CROSS APPLY STRING_SPLIT(n.Tags, N',') v
                WHERE n.Tags IS NOT NULL AND LTRIM(RTRIM(v.value)) NOT IN (N'', N'onenote');

                INSERT INTO Tags (Name) SELECT DISTINCT Name FROM #tg;

                INSERT INTO NoteTags (NoteId, TagId)
                SELECT DISTINCT t.NoteId, g.Id FROM #tg t JOIN Tags g ON g.Name = t.Name;

                DROP TABLE #tg;
                """);

            migrationBuilder.DropColumn(
                name: "SourceFile",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Notes");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_SourceId",
                table: "Notes",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTags_TagId",
                table: "NoteTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_Kind_Name_Section",
                table: "Sources",
                columns: new[] { "Kind", "Name", "Section" },
                unique: true,
                filter: "[Section] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Sources_SourceId",
                table: "Notes",
                column: "SourceId",
                principalTable: "Sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "Notes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Notes",
                type: "nvarchar(max)",
                nullable: true);

            // Geri alırken metinler tablolardan yeniden kurulur.
            migrationBuilder.Sql("""
                UPDATE n SET SourceFile = CASE s.Kind WHEN N'OneNote' THEN N'OneNote: ' + s.Name + N' › ' + s.Section ELSE s.Name END
                FROM Notes n JOIN Sources s ON s.Id = n.SourceId;

                UPDATE n SET Tags = (SELECT STRING_AGG(g.Name, N', ') FROM NoteTags nt JOIN Tags g ON g.Id = nt.TagId WHERE nt.NoteId = n.Id)
                FROM Notes n;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Sources_SourceId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "NoteTags");

            migrationBuilder.DropTable(
                name: "Sources");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Notes_SourceId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Notes");
        }
    }
}
