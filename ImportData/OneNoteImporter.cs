using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Services;
using Microsoft.EntityFrameworkCore;

namespace ABAPKnowledgeBase.ImportData;

/// <summary>
/// scripts/Export-OneNote.ps1 ile dışa aktarılan OneNote sayfalarını notlara çevirir.
/// Varsayılan: sadece önizleme yazar (onizleme.md), veritabanına dokunmaz.
/// apply=true: mevcut notları yedekler, veritabanını sıfırdan kurar ve OneNote notlarını ekler.
/// add=true: veritabanına dokunmadan sadece eksik sayfaları bulur; apply ile birlikte bunları ekler.
/// Bu modda var olan notlar, sıralar ve firmalar değişmez; Projeler kategorisine giden sayfalar eklenmez.
/// </summary>
public static partial class OneNoteImporter
{
    private sealed class PageInfo
    {
        public string File { get; set; } = "";
        public string Notebook { get; set; } = "";
        public string SectionGroups { get; set; } = "";
        public string Section { get; set; } = "";
        public int Order { get; set; }
        public int Level { get; set; }
        public string Title { get; set; } = "";
        public string Created { get; set; } = "";
        public string Modified { get; set; } = "";
    }

    private sealed record Planned(PageInfo Page, Note Note, string Category, string Method);

    // Eşleme dosyasında bu değer, sayfanın/bölümün hiç aktarılmayacağı anlamına gelir.
    private const string SkipMarker = "__ATLA__";

    public static async Task RunAsync(NotesDbContext db, ClassificationService classifier, string importDir, bool apply, bool add = false)
    {
        var exportDir = Path.Combine(importDir, "onenote");
        var manifestPath = Path.Combine(exportDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Önce scripts/Export-OneNote.ps1 çalıştırılmalı", manifestPath);

        var pages = JsonSerializer.Deserialize<List<PageInfo>>(await File.ReadAllTextAsync(manifestPath)) ?? [];
        // OneNote başlıklarında bölünmez boşluk (NBSP) olabilir; eşleme ve arama için normal boşluğa çevrilir.
        foreach (var p in pages) p.Title = p.Title.Replace(' ', ' ');
        var mappingPath = Path.Combine(importDir, "onenote-kategoriler.json");
        if (!File.Exists(mappingPath))
            throw new FileNotFoundException("Kategori eşlemesi yok: onenote-kategoriler.example.json dosyasını onenote-kategoriler.json adıyla kopyalayıp düzenle", mappingPath);
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(mappingPath)) ?? [];

        var skipped = pages.Where(p => Lookup(p, mapping).Value == SkipMarker).ToList();
        foreach (var g in skipped.GroupBy(p => p.Notebook)) Console.WriteLine($"Atlanan: {g.Key} - {g.Count()} sayfa");
        var planned = pages.Except(skipped).Select(p => Plan(p, exportDir, mapping, classifier)).ToList();
        if (add)
        {
            await AddMissingAsync(db, planned, Path.Combine(importDir, "yedek"), Path.Combine(exportDir, "onizleme.md"), apply);
            return;
        }
        await WritePreviewAsync(planned, Path.Combine(exportDir, "onizleme.md"));

        foreach (var g in planned.GroupBy(p => p.Category).OrderByDescending(g => g.Count()))
            Console.WriteLine($"{g.Count(),4}  {g.Key}");
        Console.WriteLine($"Toplam {planned.Count} sayfa, {planned.Sum(p => p.Note.Images.Count)} resim. Önizleme: {Path.Combine(exportDir, "onizleme.md")}");

        if (!apply)
        {
            Console.WriteLine("Önizleme modu: veritabanı değiştirilmedi. Uygulamak için --apply ekle.");
            return;
        }

        var backup = await BackupAsync(db, Path.Combine(importDir, "yedek"));
        Console.WriteLine($"Mevcut notlar yedeklendi: {backup}");

        // Veritabanı sıfırdan, migration'larla kurulur.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        var names = CategorySeed.All.Concat(planned.Select(p => p.Category)).Distinct().ToList();
        db.Categories.AddRange(names.Select(n => new Category { Name = n }));
        await db.SaveChangesAsync();

        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
        // Projeler kategorisine giden sayfalarda OneNote bölüm adı firma olur ("projeler / Firma A" -> "Firma A").
        var companies = planned.Where(p => p.Category == CategorySeed.Projects)
            .Select(p => p.Page.Section.Trim()).Distinct()
            .ToDictionary(n => n, n => new Company { Name = n.Length > 100 ? n[..100] : n });
        db.Companies.AddRange(companies.Values);
        // Her kategoride sayfalar OneNote'taki sırayla dizilir (sonradan sürükle-bırak ile değiştirilebilir).
        var nextOrder = new Dictionary<string, int>();
        foreach (var p in planned)
        {
            p.Note.CategoryId = categories[p.Category];
            p.Note.SortOrder = nextOrder[p.Category] = nextOrder.GetValueOrDefault(p.Category) + 1;
            if (p.Category == CategorySeed.Projects) p.Note.Company = companies[p.Page.Section.Trim()];
            db.Notes.Add(p.Note);
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"OneNote içe aktarma bitti. Eklenen not: {planned.Count}.");
    }

    private static Planned Plan(PageInfo page, string exportDir, Dictionary<string, string> mapping, ClassificationService classifier)
    {
        var note = Convert(page, Path.Combine(exportDir, page.File));
        var section = string.IsNullOrEmpty(page.SectionGroups) ? page.Section : $"{page.SectionGroups}/{page.Section}";
        string? category = null, method;
        var (key, value) = Lookup(page, mapping);
        var ocr = string.Join(' ', note.Images.Select(i => i.OcrText));
        var best = classifier.Suggest($"{note.Title} {note.Body} {ocr}").FirstOrDefault();
        if (!string.IsNullOrEmpty(value))
        {
            category = value;
            method = !key!.EndsWith("/*") ? (key == $"{page.Notebook}/{section}" ? "bölüm" : "sayfa") : key == $"{page.Notebook}/*" ? "defter" : "grup";
        }
        else if (best.Category is not null) { category = best.Category; method = $"öneri: {best.Evidence}"; }
        else method = "VARSAYILAN (öneri yok)";
        // Eşleme ile içerik güçlü biçimde çelişiyorsa önizlemede işaretle; karar kullanıcının.
        if (category is not null && method is "bölüm" or "grup" or "defter" && best.Category is not null && best.Category != category && best.Score >= 6)
            method += $" ⚠ içerik: {best.Category} ({best.Evidence})";
        return new Planned(page, note, category ?? "ABAP Temelleri", method);
    }

    // Eşlemede bu sayfa için geçerli ilk kayıt: tek sayfa ("defter/bölüm/başlık") > bölüm ("defter/bölüm") >
    // bölüm grubu ("defter/grup/*") > defter ("defter/*"). Boş değer: kategoriyi sınıflandırıcı seçer.
    private static (string? Key, string? Value) Lookup(PageInfo page, Dictionary<string, string> mapping)
    {
        var section = string.IsNullOrEmpty(page.SectionGroups) ? page.Section : $"{page.SectionGroups}/{page.Section}";
        var keys = new List<string> { $"{page.Notebook}/{section}/{page.Title.Trim()}", $"{page.Notebook}/{section}" };
        var groups = page.SectionGroups.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = groups.Length; i > 0; i--) keys.Add($"{page.Notebook}/{string.Join('/', groups[..i])}/*");
        keys.Add($"{page.Notebook}/*");
        foreach (var key in keys)
            if (mapping.TryGetValue(key, out var value)) return (key, value);
        return (null, null);
    }

    // ---------- Sadece eksikleri ekleme ----------

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
    private static string Normalized(string? body) => WhiteSpace().Replace(body ?? "", " ").Trim();

    private static async Task AddMissingAsync(NotesDbContext db, List<Planned> planned, string backupDir, string previewPath, bool apply)
    {
        var existing = await db.Notes.AsNoTracking().Select(n => new { n.Id, n.Title, n.Body, n.SourceFile, n.UpdatedAt }).ToListAsync();
        var bySource = existing.GroupBy(n => $"{n.SourceFile}|{n.Title.Trim()}").ToDictionary(g => g.Key, g => g.ToList());
        var known = existing.Select(n => $"{n.Title.Trim()}|{Normalized(n.Body)}").ToHashSet();

        List<Planned> missing = [], changed = [];
        // Bizde içi boş olan ama OneNote'ta sonradan doldurulan sayfalar: kaybedilecek bir şey olmadığı için doldurulur.
        var fill = new List<(int Id, Planned Page)>();
        int same = 0, duplicates = 0, projects = 0;
        foreach (var p in planned)
        {
            if (p.Category == CategorySeed.Projects) { projects++; continue; }
            var body = Normalized(p.Note.Body);
            if (bySource.TryGetValue($"{p.Note.SourceFile}|{p.Note.Title.Trim()}", out var rows) && rows.Count > 0)
            {
                // Aynı bölümde aynı başlıkta birden çok sayfa olabilir: önce içeriği aynı olanla eşleştir.
                var match = rows.FirstOrDefault(r => Normalized(r.Body) == body);
                if (match is not null) { rows.Remove(match); same++; continue; }
                match = rows[0]; rows.RemoveAt(0);
                // OneNote'ta sonradan değişen sayfa üzerine yazılmaz, sadece raporlanır (notta elle düzenleme olabilir).
                if (string.IsNullOrWhiteSpace(match.Body)) fill.Add((match.Id, p));
                else if (p.Note.UpdatedAt > match.UpdatedAt) changed.Add(p);
                else same++;
                continue;
            }
            // Başlıksız ve içeriksiz boş sayfalar eklenmez.
            if (body == "" && p.Note.Images.Count == 0) continue;
            // Başka bölümde birebir aynı başlık+içerik varsa aynı not iki kere eklenmez.
            if (!known.Add($"{p.Note.Title.Trim()}|{body}")) { duplicates++; continue; }
            missing.Add(p);
        }

        var sb = new StringBuilder("# Eksik sayfalar\n\n| # | Defter / Bölüm | Başlık | Kategori | Neden | Resim | Karakter |\n|---|---|---|---|---|---|---|\n");
        var i = 0;
        foreach (var p in missing)
            sb.Append($"| {++i} | {p.Page.Notebook} / {p.Page.SectionGroups} / {p.Page.Section} | {p.Note.Title.Replace("|", "/")} | {p.Category} | {p.Method.Replace("|", "/")} | {p.Note.Images.Count} | {p.Note.Body.Length} |\n");
        sb.Append("\n# OneNote'ta sonradan değişen sayfalar (üzerine yazılmadı)\n\n");
        foreach (var p in changed) sb.Append($"- {p.Page.Notebook} / {p.Page.Section} / {p.Note.Title}\n");
        await File.WriteAllTextAsync(previewPath, sb.ToString());

        foreach (var g in missing.GroupBy(p => p.Category).OrderByDescending(g => g.Count()))
            Console.WriteLine($"{g.Count(),4}  {g.Key}");
        Console.WriteLine($"Eksik: {missing.Count} sayfa, {missing.Sum(p => p.Note.Images.Count)} resim. Zaten var: {same}. Doldurulacak boş not: {fill.Count}. Kopya: {duplicates}. Proje (eklenmedi): {projects}. OneNote'ta değişmiş: {changed.Count}.");
        Console.WriteLine($"Önizleme: {previewPath}");
        if (!apply)
        {
            Console.WriteLine("Önizleme modu: veritabanı değiştirilmedi. Eklemek için --apply de ekle.");
            return;
        }

        Console.WriteLine($"Mevcut notlar yedeklendi: {await BackupAsync(db, backupDir)}");
        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
        // Yeni sayfalar kategorilerinin sonuna, OneNote sırasıyla eklenir; mevcut sıra değişmez.
        var maxOrder = await db.Notes.GroupBy(n => n.CategoryId).Select(g => new { g.Key, Max = g.Max(n => n.SortOrder) }).ToDictionaryAsync(x => x.Key, x => x.Max);
        foreach (var p in missing)
        {
            var categoryId = categories[p.Category];
            p.Note.CategoryId = categoryId;
            p.Note.SortOrder = maxOrder[categoryId] = maxOrder.GetValueOrDefault(categoryId) + 1;
            db.Notes.Add(p.Note);
        }
        foreach (var (id, p) in fill)
        {
            var note = await db.Notes.Include(n => n.Images).FirstAsync(n => n.Id == id);
            note.Body = p.Note.Body;
            note.UpdatedAt = p.Note.UpdatedAt;
            note.Images = p.Note.Images;
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"Eklenen not: {missing.Count}. Doldurulan boş not: {fill.Count}.");
    }

    // ---------- OneNote sayfa XML'i -> Note ----------

    private static Note Convert(PageInfo page, string path)
    {
        var root = XDocument.Load(path).Root!;
        XNamespace one = root.Name.Namespace;
        var writer = new BodyWriter();
        var images = new List<NoteImage>();

        // Sayfadaki serbest kutular (metin kutuları ve resimler) yukarıdan aşağıya, soldan sağa okunur.
        var blocks = root.Elements()
            .Where(e => e.Name == one + "Outline" || e.Name == one + "Image")
            .OrderBy(e => Pos(e, one, "y")).ThenBy(e => Pos(e, one, "x"));
        foreach (var block in blocks)
        {
            if (block.Name == one + "Image") AddImage(block, one, writer, images);
            else foreach (var oe in block.Elements(one + "OEChildren").Elements(one + "OE")) WriteOE(oe, one, 0, writer, images);
            writer.EndBlock();
        }

        var title = page.Title.Trim();
        if (title == "") title = $"{page.Section} (başlıksız)";
        var section = string.IsNullOrEmpty(page.SectionGroups) ? page.Section : $"{page.SectionGroups}/{page.Section}";
        return new Note
        {
            Title = title.Length > 180 ? title[..180] : title,
            Body = writer.ToString(),
            Code = null,
            Tags = $"onenote, {page.Section}",
            SourceFile = $"OneNote: {page.Notebook} › {section}",
            SourcePage = page.Order,
            CreatedAt = ParseDate(page.Created),
            UpdatedAt = ParseDate(page.Modified),
            Images = images
        };
    }

    private static void WriteOE(XElement oe, XNamespace one, int depth, BodyWriter writer, List<NoteImage> images)
    {
        var mono = IsMono(oe.Attribute("style")?.Value);
        var list = oe.Element(one + "List");
        var prefix = list?.Element(one + "Number")?.Attribute("text")?.Value is { } number ? number + " "
            : list?.Element(one + "Bullet") is not null ? "• " : "";

        foreach (var child in oe.Elements())
        {
            if (child.Name == one + "T")
            {
                var html = child.Value;
                var text = HtmlToText(html);
                if (mono || IsMono(html)) writer.Code(text);
                else writer.Text(prefix + text, depth);
                prefix = "";
            }
            else if (child.Name == one + "Table") WriteTable(child, one, depth, writer, images);
            else if (child.Name == one + "Image") AddImage(child, one, writer, images);
            else if (child.Name == one + "OEChildren")
                foreach (var sub in child.Elements(one + "OE")) WriteOE(sub, one, depth + 1, writer, images);
        }
    }

    // Tablo satırları "hücre | hücre" biçiminde düz metne çevrilir; hücredeki resimler tablodan sonra eklenir.
    private static void WriteTable(XElement table, XNamespace one, int depth, BodyWriter writer, List<NoteImage> images)
    {
        var cellImages = new List<XElement>();
        foreach (var row in table.Elements(one + "Row"))
        {
            var cells = row.Elements(one + "Cell").Select(cell =>
            {
                cellImages.AddRange(cell.Descendants(one + "Image"));
                return string.Join(" ", cell.Descendants(one + "T").Select(t => HtmlToText(t.Value).Trim()).Where(t => t != ""));
            }).ToList();
            if (cells.Any(c => c != "")) writer.Text(string.Join(" | ", cells), depth);
        }
        foreach (var img in cellImages) AddImage(img, one, writer, images);
    }

    private static void AddImage(XElement image, XNamespace one, BodyWriter writer, List<NoteImage> images)
    {
        var data = image.Element(one + "Data")?.Value;
        if (string.IsNullOrWhiteSpace(data)) return;
        var format = (image.Attribute("format")?.Value ?? "png").ToLowerInvariant();
        var ocr = image.Element(one + "OCRData")?.Element(one + "OCRText")?.Value.Trim();
        var order = images.Count + 1;
        images.Add(new NoteImage
        {
            Order = order,
            ContentType = format switch { "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif", "bmp" => "image/bmp", "emf" => "image/emf", _ => "image/png" },
            Data = System.Convert.FromBase64String(data),
            OcrText = string.IsNullOrEmpty(ocr) ? null : ocr
        });
        writer.Image(order);
    }

    [GeneratedRegex(@"font-family:\s*['""]?(Courier|Consolas|Lucida Console|Cascadia|monospace)", RegexOptions.IgnoreCase)]
    private static partial Regex MonoFont();
    private static bool IsMono(string? styleOrHtml) => styleOrHtml is not null && MonoFont().IsMatch(styleOrHtml);

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex Br();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    // OneNote metni HTML parçasıdır: satır sonları <br>, boşluklar &nbsp; olarak gelir.
    private static string HtmlToText(string html)
    {
        var s = html.Replace("\r\n", " ").Replace('\n', ' ');
        s = Br().Replace(s, "\n");
        s = Tag().Replace(s, "");
        return WebUtility.HtmlDecode(s).Replace(' ', ' ').TrimEnd();
    }

    private static double Pos(XElement e, XNamespace one, string axis) =>
        double.TryParse(e.Element(one + "Position")?.Attribute(axis)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.UtcNow;

    /// <summary>Gövdeyi satır satır kurar; ardışık kod satırlarını tek ``` bloğunda toplar.</summary>
    private sealed class BodyWriter
    {
        private readonly List<string> _lines = [];
        private bool _inCode;

        public void Text(string text, int depth)
        {
            CloseCode();
            var indent = new string(' ', depth * 2);
            foreach (var line in text.Split('\n')) _lines.Add(line.Trim() == "" ? "" : indent + line);
        }

        public void Code(string text)
        {
            if (!_inCode) { TrimBlank(); if (_lines.Count > 0) _lines.Add(""); _lines.Add("```"); _inCode = true; }
            _lines.AddRange(text.Split('\n'));
        }

        public void Image(int order)
        {
            CloseCode();
            _lines.Add($"[resim:{order}]");
        }

        public void EndBlock()
        {
            CloseCode();
            if (_lines.Count > 0 && _lines[^1] != "") _lines.Add("");
        }

        private void CloseCode()
        {
            if (!_inCode) return;
            while (_lines[^1].Trim() == "" && _lines[^1] != "```") _lines.RemoveAt(_lines.Count - 1);
            if (_lines[^1] == "```") _lines.RemoveAt(_lines.Count - 1); // boş kod bloğu
            else _lines.Add("```");
            _lines.Add("");
            _inCode = false;
        }

        private void TrimBlank() { while (_lines.Count > 0 && _lines[^1] == "") _lines.RemoveAt(_lines.Count - 1); }

        public override string ToString()
        {
            CloseCode();
            TrimBlank();
            // Üç ve daha fazla boş satırı ikiye indir.
            var sb = new StringBuilder();
            var blank = 0;
            foreach (var line in _lines)
            {
                blank = line.Trim() == "" ? blank + 1 : 0;
                if (blank <= 1) sb.Append(line.TrimEnd()).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }

    // ---------- Önizleme ve yedek ----------

    private static async Task WritePreviewAsync(List<Planned> planned, string path)
    {
        var sb = new StringBuilder("# OneNote içe aktarma önizlemesi\n\n| # | Defter / Bölüm | Başlık | Kategori | Neden | Resim | Karakter |\n|---|---|---|---|---|---|---|\n");
        var i = 0;
        foreach (var p in planned)
            sb.Append($"| {++i} | {p.Page.Notebook} / {p.Page.Section} | {p.Note.Title.Replace("|", "/")} | {p.Category} | {p.Method.Replace("|", "/")} | {p.Note.Images.Count} | {p.Note.Body.Length} |\n");
        await File.WriteAllTextAsync(path, sb.ToString());

        // Dönüştürülmüş gövdeler de yazılır; aktarmadan önce metin/kod ayrımı kontrol edilebilir.
        var bodies = new StringBuilder();
        i = 0;
        foreach (var p in planned)
            bodies.Append($"\n\n==================== {++i}. {p.Note.Title}  [{p.Category}]\n\n{p.Note.Body}");
        await File.WriteAllTextAsync(Path.ChangeExtension(path, null) + "-govdeler.txt", bodies.ToString());
    }

    private static async Task<string> BackupAsync(NotesDbContext db, string dir)
    {
        Directory.CreateDirectory(dir);
        // Eski şemada NoteImages tablosu olmayabilir; sadece not alanları okunur.
        var notes = await db.Notes.AsNoTracking().Select(n => new
        {
            n.Title, n.Body, n.Code, n.Tags, Category = n.Category!.Name,
            n.SourceFile, n.SourcePage, n.CreatedAt, n.UpdatedAt
        }).ToListAsync();
        var path = Path.Combine(dir, $"notlar-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(notes, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        return path;
    }
}
