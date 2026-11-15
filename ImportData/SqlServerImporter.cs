using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Services;
using Microsoft.EntityFrameworkCore;

namespace ABAPKnowledgeBase.ImportData;

/// <summary>
/// Tek seferlik geçiş: eski SQL Server (SQLEXPRESS) veritabanındaki tüm kategorileri, firmaları,
/// notları ve resimleri yeni SQLite veritabanına Id'leriyle birlikte kopyalar.
/// Kaynak veritabanına sadece okur, hiçbir şeyi değiştirmez.
///   dotnet run -- --import-sqlserver
/// Kaynak bağlantı: appsettings.json → ConnectionStrings:SqlServer (varsayılan .\SQLEXPRESS).
/// </summary>
public static class SqlServerImporter
{
    // quiet=true: site açılışındaki otomatik deneme. SQL Server yoksa ya da içinde not yoksa sessizce geçer.
    public static async Task<bool> RunAsync(NotesDbContext target, string? sourceConnectionString, bool quiet = false)
    {
        if (string.IsNullOrWhiteSpace(sourceConnectionString))
        {
            if (!quiet) Console.WriteLine("ConnectionStrings:SqlServer tanımlı değil.");
            return false;
        }

        var targetNotes = await target.Notes.CountAsync();
        if (targetNotes > 0)
        {
            if (!quiet) Console.WriteLine($"SQLite veritabanında zaten {targetNotes} not var; aktarım iki kez yapılmaz. Hiçbir şey değişmedi.");
            return false;
        }

        var options = new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(sourceConnectionString).Options;
        await using var source = new NotesDbContext(options);
        return await CopyAsync(source, target, quiet);
    }

    // Kaynak SQL Server'daki NotesDbContext'tir. Notlar şemadan bağımsız okunur (bkz. ReadNotesAsync).
    public static async Task<bool> CopyAsync(NotesDbContext source, NotesDbContext target, bool quiet = false)
    {
        if (!quiet) Console.WriteLine("SQL Server'a bağlanılıyor...");
        if (!await source.Database.CanConnectAsync())
        {
            if (!quiet) Console.WriteLine("SQL Server'a bağlanılamadı. 'SQL Server (SQLEXPRESS)' servisi çalışıyor mu? Bağlantı: ConnectionStrings:SqlServer");
            return false;
        }

        var notes = await ReadNotesAsync(source);
        if (notes.Count == 0)
        {
            if (!quiet) Console.WriteLine("SQL Server veritabanında not yok; aktarılacak bir şey yok.");
            return false;
        }
        Console.WriteLine("Eski SQL Server veritabanı bulundu; notlar SQLite'a aktarılıyor (tek seferlik)...");
        var categories = await source.Categories.AsNoTracking().ToListAsync();
        var companies = await source.Companies.AsNoTracking().ToListAsync();
        var imageCount = await source.NoteImages.CountAsync();
        Console.WriteLine($"Kaynak: {categories.Count} kategori, {companies.Count} firma, {notes.Count} not, {imageCount} resim.");

        await using var tx = await target.Database.BeginTransactionAsync();
        // Açılışta eklenen varsayılan kategoriler, kaynaktakilerle aynı Id'leri alsın diye silinir (not yok).
        await target.Categories.ExecuteDeleteAsync();
        await target.Companies.ExecuteDeleteAsync();
        target.ChangeTracker.Clear(); // silinen varsayılan kategoriler bellekte takipte kalmasın
        target.Categories.AddRange(categories);
        target.Companies.AddRange(companies);
        // Etiket ve kaynak metni Tags/NoteTags/Sources tablolarına çevrilir.
        var links = await NoteLinks.LoadAsync(target);
        foreach (var note in notes) links.Apply(note, note.TagsText, note.SourceText);
        target.Notes.AddRange(notes);
        await target.SaveChangesAsync();
        target.ChangeTracker.Clear();

        // Resimler büyük olabilir: notlar 50'şerlik gruplar hâlinde taşınır.
        foreach (var chunk in notes.Select(n => n.Id).Chunk(50))
        {
            var images = await source.NoteImages.AsNoTracking().Where(i => chunk.Contains(i.NoteId)).ToListAsync();
            target.NoteImages.AddRange(images);
            await target.SaveChangesAsync();
            target.ChangeTracker.Clear();
        }
        await tx.CommitAsync();

        // Doğrulama: sayılar ve içerik toplamı birebir tutmalı.
        var check = new[]
        {
            ("kategori", categories.Count, await target.Categories.CountAsync()),
            ("firma", companies.Count, await target.Companies.CountAsync()),
            ("not", notes.Count, await target.Notes.CountAsync()),
            ("resim", imageCount, await target.NoteImages.CountAsync()),
            ("etiketli", notes.Count(n => NoteLinks.ParseTags(n.TagsText).Count > 0), await target.Notes.CountAsync(n => n.NoteTags.Any())),
            ("kaynaklı", notes.Count(n => NoteLinks.ParseSource(n.SourceText) is not null), await target.Notes.CountAsync(n => n.SourceId != null)),
        };
        var sourceBodyLength = notes.Sum(n => (long)n.Body.Length);
        var targetBodyLength = (await target.Notes.Select(n => n.Body).ToListAsync()).Sum(b => (long)b.Length);
        var ok = check.All(c => c.Item2 == c.Item3) && sourceBodyLength == targetBodyLength;
        foreach (var (name, src, dst) in check) Console.WriteLine($"  {name,-9} SQL Server: {src,5}  SQLite: {dst,5}  {(src == dst ? "✓" : "FARKLI")}");
        Console.WriteLine($"  metin     SQL Server: {sourceBodyLength} karakter  SQLite: {targetBodyLength} karakter  {(sourceBodyLength == targetBodyLength ? "✓" : "FARKLI")}");
        Console.WriteLine(ok ? "Aktarım tamamlandı, her şey birebir tuttu." : "UYARI: Sayılar tutmadı, yukarıdaki satırlara bak. SQL Server'daki veriye dokunulmadı.");
        return ok;
    }

    // Eski SQL Server iki şemadan birinde olabilir: etiket/kaynak Notes'ta metin sütunu (Tags, SourceFile)
    // ya da ayrı tablolarda (Tags/NoteTags/Sources, EtiketKaynakTablolari migration'ı uygulanmış).
    // İki durumda da notlar etiket/kaynak metniyle (TagsText/SourceText) okunur.
    private static async Task<List<Note>> ReadNotesAsync(NotesDbContext source)
    {
        var conn = source.Database.GetDbConnection();
        await source.Database.OpenConnectionAsync();
        try
        {
            await using var probe = conn.CreateCommand();
            probe.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Notes' AND COLUMN_NAME = 'SourceFile'";
            var textColumns = Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0;

            const string columns = "n.Id, n.Title, n.Body, n.Code, n.CategoryId, n.CompanyId, n.SourcePage, n.CreatedAt, n.UpdatedAt, n.SortOrder";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = textColumns
                ? $"SELECT {columns}, n.Tags, n.SourceFile FROM Notes n ORDER BY n.Id"
                : $"""
                   SELECT {columns},
                          (SELECT STRING_AGG(t.Name, N',') FROM NoteTags nt JOIN Tags t ON t.Id = nt.TagId WHERE nt.NoteId = n.Id),
                          CASE WHEN s.Kind = N'{Source.OneNote}' THEN N'OneNote: ' + s.Name + N' › ' + ISNULL(s.Section, N'') ELSE s.Name END
                   FROM Notes n LEFT JOIN Sources s ON s.Id = n.SourceId ORDER BY n.Id
                   """;
            var notes = new List<Note>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                notes.Add(new Note
                {
                    Id = r.GetInt32(0), Title = r.GetString(1), Body = r.GetString(2),
                    Code = r.IsDBNull(3) ? null : r.GetString(3),
                    CategoryId = r.GetInt32(4),
                    CompanyId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    SourcePage = r.IsDBNull(6) ? null : r.GetInt32(6),
                    CreatedAt = r.GetDateTime(7), UpdatedAt = r.GetDateTime(8), SortOrder = r.GetInt32(9),
                    // Her nota kendiliğinden eklenen "onenote" etiketi taşınmaz: kaynak bilgisinden görülüyor.
                    TagsText = r.IsDBNull(10) ? null : string.Join(", ", NoteLinks.ParseTags(r.GetString(10))
                        .Where(t => !t.Equals("onenote", StringComparison.OrdinalIgnoreCase))),
                    SourceText = r.IsDBNull(11) ? null : r.GetString(11),
                });
            return notes;
        }
        finally
        {
            await source.Database.CloseConnectionAsync();
        }
    }
}
