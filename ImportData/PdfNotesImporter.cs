using System.Text.Json;
using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using Microsoft.EntityFrameworkCore;

namespace ABAPKnowledgeBase.ImportData;

public static class PdfNotesImporter
{
    private sealed class ImportedNote
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string Category { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public int SourcePage { get; set; }
        public string Tags { get; set; } = "";
    }

    public static async Task RunAsync(NotesDbContext db, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Not veri dosyası bulunamadı", path);

        var data = JsonSerializer.Deserialize<List<ImportedNote>>(
            await File.ReadAllTextAsync(path)) ?? [];

        var categories = await db.Categories.ToListAsync();
        int added = 0, skipped = 0;
        // Tek tek sorgulamak yerine kaynak/sayfa anahtarlarını bir kere oku.
        var existing = (await db.Notes.AsNoTracking()
            .Where(n => n.SourceFile != null && n.SourcePage != null)
            .Select(n => new { n.SourceFile, n.SourcePage }).ToListAsync())
            .Select(n => $"{n.SourceFile}|{n.SourcePage}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in data)
        {
            if (item.Category == "Gerçek Projeler") continue;
            var cat = categories.FirstOrDefault(c => c.Name == item.Category);
            if (cat is null)
            {
                Console.WriteLine($"Kategori yok, atlandı: {item.Category}");
                skipped++; continue;
            }
            var key = $"{item.SourceFile}|{item.SourcePage}";
            if (!existing.Add(key)) { skipped++; continue; }

            db.Notes.Add(new Note
            {
                Title = item.Title.Length > 180 ? item.Title[..180] : item.Title,
                Body = item.Body,
                Code = "",
                Tags = item.Tags,
                CategoryId = cat.Id,
                SourceFile = item.SourceFile,
                SourcePage = item.SourcePage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            added++;
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"PDF içe aktarma bitti. Yeni: {added}, atlanan/önceden var: {skipped}.");
    }
}
