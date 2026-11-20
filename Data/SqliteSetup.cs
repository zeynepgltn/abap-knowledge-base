using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ABAPKnowledgeBase.Data;

// Veritabanı: SQLite, tek dosya. Kurulum gerektirmez; yedek = dosyanın kopyası.
// Dosya git klasöründe ve OneDrive'da DEĞİL, bilgisayarın yerel uygulama verisinde durur
// (OneDrive açık bir veritabanı dosyasını senkronlarken bozabilir). OneDrive'a sadece yedekler gider.
public static class SqliteSetup
{
    public static string DatabasePath(IConfiguration config)
    {
        var configured = config.GetValue<string>("Database:Path");
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ABAPKnowledgeBase", "abap-notlar.db")
            : Environment.ExpandEnvironmentVariables(configured.Trim());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public static string ConnectionString(IConfiguration config) =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath(config) }.ToString();
}

// Aramada "bir metin diğerinin içinde geçiyor mu" kontrolü.
// SQL Server aramada büyük/küçük harfe bakmıyordu; SQLite'ın instr/LIKE'ı ise Türkçe harflerde
// büyük/küçük ayırır. Bu fonksiyon iki metni de katlar: büyük/küçük fark etmez ve İ/I/ı/i aynı sayılır
// ("itab" = "ITAB" = "İTAB"). LINQ'te TextMatch.Contains(...) yazılır, SQLite'ta tr_contains çalışır.
//
// Joker (SAP seçim ekranındaki gibi): * herhangi sayıda harf, + tam bir harf.
// Joker olan kelime bir sözcüğün tamamına uyar: "rs*abap*" = rs ile başlayıp içinde abap geçen sözcük
// (RS_ABAP_SOURCE_SCAN), "*çelik*" = içinde çelik geçen (SQL'deki LIKE '%ÇELİK%'), "z*" = z ile başlayan.
// Sözcük harfleri: harf, rakam, _ ve / (ABAP adları ve isim alanları).
public static class TextMatch
{
    public static bool Contains(string? text, string term) => Find(text, term) is { Index: >= 0 };

    public static string Fold(string s) =>
        s.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

    public static bool HasWildcard(string term) => term.IndexOfAny(['*', '+']) >= 0;

    // Fold harf harf çevirir, uzunluk değişmez; bulunan konum orijinal metinde de geçerlidir.
    public static (int Index, int Length) Find(string? text, string term, int start = 0)
    {
        if (string.IsNullOrEmpty(text) || term.Length == 0 || start >= text.Length) return (-1, 0);
        var folded = Fold(text);
        if (!HasWildcard(term)) return (folded.IndexOf(Fold(term), start, StringComparison.Ordinal), term.Length);
        try
        {
            var m = Pattern(term).Match(folded, start);
            return m.Success ? (m.Index, m.Length) : (-1, 0);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return (-1, 0); }
    }

    private const string WordChar = @"[\p{L}\p{N}_/]";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.RegularExpressions.Regex> Patterns = new();

    private static System.Text.RegularExpressions.Regex Pattern(string term) => Patterns.GetOrAdd(Fold(term), t =>
    {
        var body = new System.Text.StringBuilder();
        foreach (var ch in t)
            body.Append(ch switch { '*' => WordChar + "*?", '+' => WordChar, _ => System.Text.RegularExpressions.Regex.Escape(ch.ToString()) });
        // Kelimenin iki ucu sözcük sınırına oturur; uçtaki * sınırı serbest bırakır.
        var pattern = $"(?<!{WordChar}){body}(?!{WordChar})";
        if (t.EndsWith('*')) pattern = $"(?<!{WordChar}){body}{WordChar}*";
        return new System.Text.RegularExpressions.Regex(pattern,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    });
}

// Her bağlantı açıldığında tr_contains fonksiyonunu SQLite'a tanıtır.
public sealed class SqliteFunctionsInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    private static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
            sqlite.CreateFunction<string?, string?, bool>("tr_contains",
                (text, term) => term is not null && TextMatch.Contains(text, term), isDeterministic: true);
    }
}
