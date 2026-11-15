using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABAPKnowledgeBase.Data.Migrations
{
    /// <summary>
    /// Notes.Tags (virgüllü metin) -> Tags + NoteTags, Notes.SourceFile (metin) -> Sources + Notes.SourceId.
    /// Var olan veriler taşınır, eski sütunlar en son silinir. Her nota kendiliğinden eklenen "onenote"
    /// etiketi taşınmaz: notun OneNote'tan geldiği artık kaynak bilgisinden görülüyor.
    /// Ayrıştırma kuralları NoteLinks.ParseTags / ParseSource ile aynıdır.
    /// </summary>
    public partial class EtiketKaynakTablolari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "Notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteTags",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
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

            // Kaynaklar: "OneNote: defter › bölüm" OneNote kaynağıdır, geri kalan her metin dosya adıdır.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE src AS
                SELECT Id,
                       CASE WHEN one THEN 'OneNote' ELSE 'Dosya' END AS Kind,
                       CASE WHEN one THEN trim(substr(substr(s, 10, sep - 10), 1, 200)) ELSE trim(substr(s, 1, 200)) END AS Name,
                       CASE WHEN one THEN trim(substr(substr(s, sep + 3), 1, 300)) END AS Section
                FROM (SELECT Id, s, instr(s, ' › ') AS sep, (s LIKE 'OneNote: %' AND instr(s, ' › ') > 10) AS one
                      FROM (SELECT Id, trim(SourceFile) AS s FROM Notes WHERE SourceFile IS NOT NULL AND trim(SourceFile) <> ''));

                INSERT INTO Sources (Kind, Name, Section)
                SELECT Kind, MIN(Name), MIN(Section) FROM src GROUP BY Kind, lower(Name), lower(Section);

                UPDATE Notes SET SourceId = (
                    SELECT s.Id FROM src x JOIN Sources s
                      ON s.Kind = x.Kind AND lower(s.Name) = lower(x.Name) AND lower(s.Section) IS lower(x.Section)
                    WHERE x.Id = Notes.Id);

                DROP TABLE src;
                """);

            // Etiketler: virgülle ayrılmış metin bölünür, boşluklar kırpılır, aynı ad (büyük/küçük harf farkı
            // gözetmeden) bir kez tutulur.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE tg AS
                WITH RECURSIVE split(NoteId, item, rest) AS (
                    SELECT Id, '', Tags || ',' FROM Notes WHERE Tags IS NOT NULL AND trim(Tags) <> ''
                    UNION ALL
                    SELECT NoteId, trim(substr(rest, 1, instr(rest, ',') - 1)), substr(rest, instr(rest, ',') + 1)
                    FROM split WHERE rest <> '')
                SELECT DISTINCT NoteId, trim(substr(item, 1, 100)) AS Name
                FROM split WHERE item <> '' AND lower(item) <> 'onenote';

                INSERT INTO Tags (Name) SELECT MIN(Name) FROM tg GROUP BY lower(Name);

                INSERT OR IGNORE INTO NoteTags (NoteId, TagId)
                SELECT t.NoteId, g.Id FROM tg t JOIN Tags g ON lower(g.Name) = lower(t.Name);

                DROP TABLE tg;
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
                unique: true);

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

            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "Notes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Notes",
                type: "TEXT",
                nullable: true);
        }
    }
}
