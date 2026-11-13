using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
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

    // Kaynak herhangi bir NotesDbContext olabilir (test için SQLite kaynakla da çalıştırılır).
    public static async Task<bool> CopyAsync(NotesDbContext source, NotesDbContext target, bool quiet = false)
    {
        if (!quiet) Console.WriteLine("SQL Server'a bağlanılıyor...");
        if (!await source.Database.CanConnectAsync())
        {
            if (!quiet) Console.WriteLine("SQL Server'a bağlanılamadı. 'SQL Server (SQLEXPRESS)' servisi çalışıyor mu? Bağlantı: ConnectionStrings:SqlServer");
            return false;
        }

        var notes = await source.Notes.AsNoTracking().OrderBy(n => n.Id).ToListAsync();
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
        };
        var sourceBodyLength = notes.Sum(n => (long)n.Body.Length);
        var targetBodyLength = (await target.Notes.Select(n => n.Body).ToListAsync()).Sum(b => (long)b.Length);
        var ok = check.All(c => c.Item2 == c.Item3) && sourceBodyLength == targetBodyLength;
        foreach (var (name, src, dst) in check) Console.WriteLine($"  {name,-9} SQL Server: {src,5}  SQLite: {dst,5}  {(src == dst ? "✓" : "FARKLI")}");
        Console.WriteLine($"  metin     SQL Server: {sourceBodyLength} karakter  SQLite: {targetBodyLength} karakter  {(sourceBodyLength == targetBodyLength ? "✓" : "FARKLI")}");
        Console.WriteLine(ok ? "Aktarım tamamlandı, her şey birebir tuttu." : "UYARI: Sayılar tutmadı, yukarıdaki satırlara bak. SQL Server'daki veriye dokunulmadı.");
        return ok;
    }
}
