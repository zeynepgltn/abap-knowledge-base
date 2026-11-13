using ABAPKnowledgeBase.Data;
using Microsoft.Data.Sqlite;

namespace ABAPKnowledgeBase.Services;

// Tam veritabanı yedeği alır, son KeepLastN yedeği tutar ve buluta (OneDrive) kopyalar.
//
// Ne zaman yedek alınır?
// - Her gün ilk açılışta (BackupScheduler)
// - Not kaydedildikten 1 dk sonra (art arda kayıtlar tek yedekte toplanır)
// - Site kapanırken, yedeklenmemiş değişiklik varsa
// Aynı günün dosyası (ABAPKnowledgeBase-yyyyMMdd.db) günün son hâliyle güncellenir;
// böylece 14 dosya = son 14 günün gün sonu hâli.
//
// Yedek, SQLite'ın "VACUUM INTO" komutuyla alınır: site açıkken bile tutarlı, sıkıştırılmış
// tam bir kopya üretir. Yedek dosyası kendi başına açılabilen bir SQLite veritabanıdır.
// Yedek hatası siteyi durdurmaz; sadece konsola detaylı yazılır.
public static class BackupService
{
    public const int DefaultKeepLastN = 14;
    public const string Prefix = "ABAPKnowledgeBase";
    public const string Extension = ".db";

    private static readonly SemaphoreSlim Gate = new(1, 1); // aynı anda tek yedek
    private static long _changeSeq;

    // Not kaydedilince (EF interceptor) tetiklenir; BackupScheduler dinler.
    public static event Action? Changed;

    public static void NotifyChanged(IConfiguration config, string contentRoot)
    {
        Interlocked.Increment(ref _changeSeq);
        SyncState.Update(LocalFolder(config, contentRoot), s => s.Dirty = true);
        Changed?.Invoke();
    }

    public static string LocalFolder(IConfiguration config, string contentRoot)
    {
        var name = config.GetValue<string>("Backup:LocalFolder");
        return string.IsNullOrWhiteSpace(name) ? Path.Combine(contentRoot, "Backups")
            : Path.IsPathRooted(name) ? name : Path.Combine(contentRoot, name);
    }

    // force=false: bugün zaten yedeklendiyse / buluttan güncellendiyse ve değişiklik yoksa atlanır.
    // force=true: her durumda yedek alınır (not değişti, kapanış, --backup, aktarımlar öncesi).
    // Dönüş: bugünlük yedeğin yolu; yedek alınamadıysa null.
    public static async Task<string?> RunDailyBackupIfNeededAsync(IConfiguration config, string contentRoot, bool force = false)
    {
        if (!(config.GetValue<bool?>("Backup:Enabled") ?? true))
        {
            Console.WriteLine("Yedekleme kapalı (Backup:Enabled=false).");
            return null;
        }

        await Gate.WaitAsync();
        try
        {
            var keepLastN = config.GetValue<int?>("Backup:KeepLastN") ?? DefaultKeepLastN;
            if (keepLastN < 1) keepLastN = DefaultKeepLastN;
            var localFolder = LocalFolder(config, contentRoot);
            Directory.CreateDirectory(localFolder);
            var backupPath = Path.Combine(localFolder, $"{Prefix}-{DateTime.Now:yyyyMMdd}{Extension}");
            var state = SyncState.Load(localFolder);
            force |= state.Dirty;

            var syncedToday = state.LastSyncedUtc?.ToLocalTime().Date == DateTime.Today;
            if (!force && (File.Exists(backupPath) || syncedToday)) return backupPath;

            var seq = Interlocked.Read(ref _changeSeq);
            await SnapshotAsync(SqliteSetup.ConnectionString(config), backupPath);
            var noteCount = await CountNotesAsync(backupPath);
            Console.WriteLine($"Yedek alındı: {backupPath} ({noteCount} not, {new FileInfo(backupPath).Length / 1024:N0} KB)");
            Prune(localFolder, keepLastN);

            // Boş veritabanı (yeni kurulum, notlar henüz gelmedi) OneDrive'a asla gönderilmez:
            // diğer bilgisayarın aynı günkü yedeğinin üzerine boş dosya yazılmasını önler.
            if (noteCount == 0)
            {
                Console.WriteLine("Veritabanında not yok; yedek OneDrive'a gönderilmedi.");
                SyncState.Update(localFolder, s => s.Dirty = false);
                return backupPath;
            }

            var synced = CopyToCloud(backupPath, config, keepLastN);
            if (synced is not null)
                SyncState.Update(localFolder, s =>
                {
                    s.LastSyncedUtc = synced;
                    // Yedek sırasında yeni kayıt geldiyse o değişiklik henüz yedekte değil.
                    if (Interlocked.Read(ref _changeSeq) == seq) s.Dirty = false;
                });
            return backupPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Yedek alınamadı. Detay:");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("Site çalışmaya devam ediyor.");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    // Veritabanının tutarlı tam kopyasını yazar. Yarım dosya geçerli yedek sanılmasın diye
    // önce geçici dosyaya yazılır, bitince taşınır.
    public static async Task SnapshotAsync(string connectionString, string targetPath)
    {
        var temp = targetPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        await using (var conn = new SqliteConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", temp);
            await cmd.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools(); // geçici dosyayı tutan bağlantı kalmasın
        File.Move(temp, targetPath, overwrite: true);
    }

    // Bir veritabanı/yedek dosyasındaki not sayısı; dosya yoksa ya da not tablosu yoksa 0.
    public static async Task<int> CountNotesAsync(string dbPath)
    {
        if (!File.Exists(dbPath)) return 0;
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'Notes') THEN (SELECT COUNT(*) FROM Notes) ELSE 0 END";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // Sadece bu uygulamanın yedekleri (ABAPKnowledgeBase-yyyyMMdd.db) budanır;
    // klasördeki başka dosyalara dokunulmaz. Tarih dosya adında olduğu için alfabetik = zamansal.
    private static void Prune(string folder, int keepLastN)
    {
        Directory.GetFiles(folder, $"{Prefix}-*{Extension}")
            .Where(p => Path.GetFileNameWithoutExtension(p).Length == Prefix.Length + 9) // -yyyyMMdd
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Skip(keepLastN)
            .ToList()
            .ForEach(p =>
            {
                try { File.Delete(p); Console.WriteLine($"Eski yedek silindi: {p}"); }
                catch { /* kilitliyse bir sonraki yedekte silinir */ }
            });
    }

    // Dönüş: buluta yazılan işaretin zamanı. Bulut yoksa da şimdiki zaman (senkron gerekmez),
    // kopyalama başarısızsa null (değişiklik "yedeklenmemiş" kalır, sonraki fırsatta tekrar denenir).
    private static DateTime? CopyToCloud(string backupPath, IConfiguration config, int keepLastN)
    {
        var cloudFolder = ResolveCloudFolder(config);
        if (string.IsNullOrWhiteSpace(cloudFolder))
        {
            Console.WriteLine("Bulut klasörü bulunamadı (OneDrive yok ya da Backup:CloudFolder tanımlı değil). Yedek sadece yerelde.");
            return DateTime.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(cloudFolder);
            var cloudPath = Path.Combine(cloudFolder, Path.GetFileName(backupPath));
            // OneDrive yarım dosyayı senkronlamasın: geçici adla kopyala, sonra yeniden adlandır.
            File.Copy(backupPath, cloudPath + ".tmp", overwrite: true);
            File.Move(cloudPath + ".tmp", cloudPath, overwrite: true);

            var marker = new CloudMarker(Path.GetFileName(backupPath), Environment.MachineName, DateTime.UtcNow, new FileInfo(cloudPath).Length);
            marker.Write(cloudFolder);
            Console.WriteLine($"Bulut kopyası çıkarıldı: {cloudPath}");
            Prune(cloudFolder, keepLastN);
            return marker.CreatedUtc;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bulut kopyası ({cloudFolder}) yapılamadı: {ex.Message}");
            return null;
        }
    }

    // Bulut: OneDrive.
    // - Windows'ta yerleşik, ek kurulum/API anahtarı yok; senkron klasöre kopyalamak yeter
    // - Microsoft hesabı zaten var (OneNote); 5 GB ücretsiz alan 14 yedek için fazlasıyla yeter
    // - Silinen/bozulan dosyalar için geri dönüşüm kutusu ve sürüm geçmişi var
    // Google Drive / Dropbox için kod değişmez: Backup:CloudFolder'a senkron klasörün yolunu yaz.
    public static string? ResolveCloudFolder(IConfiguration config)
    {
        var configured = config.GetValue<string>("Backup:CloudFolder");
        if (!string.IsNullOrWhiteSpace(configured))
            return Environment.ExpandEnvironmentVariables(configured.Trim());

        var folderName = config.GetValue<string>("Backup:CloudFolderName") ?? "ABAPKnowledgeBase-Yedekler";
        // OneDrive istemcisi bu ortam değişkenlerini ayarlar.
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Path.Combine(root, folderName);
        }
        return null;
    }
}
