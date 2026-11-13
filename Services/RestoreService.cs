using ABAPKnowledgeBase.Data;
using Microsoft.Data.Sqlite;

namespace ABAPKnowledgeBase.Services;

// Yedekten geri yükleme = veritabanı dosyasını yedekle değiştirmek.
//   Otomatik: site açılırken diğer bilgisayarın OneDrive'a bıraktığı daha yeni yedek alınır.
//   dotnet run -- --restore-from-cloud        OneDrive'daki en yeni yedek
//   dotnet run -- --restore "C:\yol\x.db"      belirli bir günün yedeği
// Değiştirmeden önce mevcut veritabanının kopyası her zaman Backups\oncesi-... olarak saklanır.
// Program.cs bunları veritabanına hiç bağlanmadan (Migrate'ten önce) çalıştırır.
public static class RestoreService
{
    // Dönüş false: site açılmamalı (iki bilgisayarda da yedeklenmemiş değişiklik var ya da yedek tam inmemiş).
    public static async Task<bool> SyncFromCloudIfNewerAsync(IConfiguration config, string contentRoot)
    {
        var cloudFolder = BackupService.ResolveCloudFolder(config);
        var marker = CloudMarker.Read(cloudFolder);
        if (marker is null || marker.Machine == Environment.MachineName) return true;

        var state = SyncState.Load(BackupService.LocalFolder(config, contentRoot));
        if (state.LastSyncedUtc >= marker.CreatedUtc) return true;

        var when = marker.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        var path = Path.Combine(cloudFolder!, marker.File);
        if (!File.Exists(path) || new FileInfo(path).Length != marker.Size)
        {
            Console.WriteLine($"Diğer bilgisayarın ({marker.Machine}, {when}) yedeği OneDrive'dan henüz tam inmemiş.");
            Console.WriteLine("Notların ezilmemesi için site açılmadı. OneDrive senkronu bitince tekrar aç.");
            return false;
        }

        // Burada hiç not yoksa kaybedilecek bir şey yok: çakışma sorulmaz, diğerininki alınır.
        if (state.Dirty && await BackupService.CountNotesAsync(SqliteSetup.DatabasePath(config)) > 0)
        {
            Console.WriteLine("DİKKAT: İki bilgisayarda da birbirinden habersiz değişiklik var:");
            Console.WriteLine($"  - {marker.Machine}: {when} tarihli yedek OneDrive'da");
            Console.WriteLine($"  - Bu bilgisayar ({Environment.MachineName}): OneDrive'a çıkmamış değişiklikler");
            Console.WriteLine("Hiçbir şey silinmedi, site açılmadı. Hangisi kalsın?");
            Console.WriteLine("  Diğer bilgisayarınki:  dotnet run -- --restore-from-cloud   (buradakinin kopyası Backups\\oncesi-... olarak saklanır)");
            Console.WriteLine("  Bu bilgisayarınki:     dotnet run -- --keep-local           (OneDrive'a buradaki hâl yazılır)");
            return false;
        }

        Console.WriteLine($"Diğer bilgisayarda ({marker.Machine}, {when}) daha yeni notlar var; bu bilgisayara alınıyor...");
        return await RunRestoreAsync(config, contentRoot, path, fromCloud: true);
    }

    // fromCloud=true: OneDrive'daki son yedekle eşitlendik. false: elle seçilen dosya; yüklenen hâl
    // yeni "son durum" sayılır ve bir sonraki yedekle OneDrive'a çıkar.
    public static async Task<bool> RunRestoreAsync(IConfiguration config, string contentRoot, string? path, bool fromCloud = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Kullanım: dotnet run -- --restore-from-cloud  veya  dotnet run -- --restore \"C:\\tam\\yol\\ABAPKnowledgeBase-yyyyMMdd.db\"");
            return false;
        }
        if (!File.Exists(path))
        {
            Console.WriteLine($"Dosya bulunamadı: {path}");
            return false;
        }

        try
        {
            if (!await IsNotesDatabaseAsync(path))
            {
                Console.WriteLine($"Bu dosya geçerli bir not yedeği değil: {path}");
                return false;
            }

            var dbPath = SqliteSetup.DatabasePath(config);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                var localFolder = BackupService.LocalFolder(config, contentRoot);
                Directory.CreateDirectory(localFolder);
                var safety = Path.Combine(localFolder, $"oncesi-{DateTime.Now:yyyyMMdd-HHmmss}{BackupService.Extension}");
                await BackupService.SnapshotAsync(SqliteSetup.ConnectionString(config), safety);
                Console.WriteLine($"Mevcut veritabanının kopyası alındı: {safety}");
            }

            // Önce geçici ada kopyala, sonra tek adımda yerine koy; yarım dosya kalmaz.
            File.Copy(path, dbPath + ".tmp", overwrite: true);
            foreach (var extra in new[] { "-wal", "-shm", "-journal" })
                if (File.Exists(dbPath + extra)) File.Delete(dbPath + extra);
            File.Move(dbPath + ".tmp", dbPath, overwrite: true);
            Console.WriteLine($"Geri yükleme tamamlandı: {Path.GetFileName(path)} -> {dbPath}");

            var marker = CloudMarker.Read(BackupService.ResolveCloudFolder(config));
            SyncState.Update(BackupService.LocalFolder(config, contentRoot), s =>
            {
                s.LastSyncedUtc = marker?.CreatedUtc ?? DateTime.UtcNow;
                s.Dirty = !fromCloud;
            });
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Geri yükleme başarısız (detay):");
            Console.WriteLine(ex.ToString());
            return false;
        }
    }

    // --restore-from-cloud: işaret dosyasının gösterdiği (en son yüklenen) yedek; yoksa adı en yeni olan.
    public static string? FindLatestCloudBackup(IConfiguration config)
    {
        var folder = BackupService.ResolveCloudFolder(config);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        var marker = CloudMarker.Read(folder);
        if (marker is not null && File.Exists(Path.Combine(folder, marker.File))) return Path.Combine(folder, marker.File);
        return Directory.GetFiles(folder, $"{BackupService.Prefix}-*{BackupService.Extension}")
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    // Yanlış dosyanın (ör. başka bir SQLite dosyası) veritabanının yerine konmasını önler.
    private static async Task<bool> IsNotesDatabaseAsync(string path)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Notes', 'Categories')";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        }
        catch { return false; }
    }
}
