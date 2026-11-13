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
public static class TextMatch
{
    public static bool Contains(string? text, string term) =>
        text is not null && Fold(text).Contains(Fold(term), StringComparison.Ordinal);

    public static string Fold(string s) =>
        s.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();
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
