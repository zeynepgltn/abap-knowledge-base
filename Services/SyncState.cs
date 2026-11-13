using System.Text.Json;

namespace ABAPKnowledgeBase.Services;

// İki bilgisayar, OneDrive üzerinden sırayla (asla aynı anda değil) aynı notları kullanır.
//
// Bu bilgisayarın durumu: Backups/senkron-durumu.json (yerel, OneDrive'a gitmez)
//   LastSyncedUtc: buluttaki hangi yedekle eşit olduğumuz (bizim aldığımız ya da geri yüklediğimiz)
//   Dirty: son yedekten sonra bu bilgisayarda not değişti mi (henüz buluta çıkmadı)
//
// Buluttaki işaret: OneDrive/ABAPKnowledgeBase-Yedekler/son-yedek.json
//   En son hangi bilgisayarın, ne zaman, hangi dosyayı yüklediğini söyler.
public sealed class SyncState
{
    public DateTime? LastSyncedUtc { get; set; }
    public bool Dirty { get; set; }

    private const string FileName = "senkron-durumu.json";
    private static readonly object Lock = new();

    public static SyncState Load(string localFolder)
    {
        lock (Lock)
        {
            try
            {
                var path = Path.Combine(localFolder, FileName);
                if (File.Exists(path)) return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path)) ?? new();
            }
            catch { /* bozuk dosya: baştan başla */ }
            return new();
        }
    }

    public static void Update(string localFolder, Action<SyncState> change)
    {
        lock (Lock)
        {
            var state = Load(localFolder);
            change(state);
            Directory.CreateDirectory(localFolder);
            File.WriteAllText(Path.Combine(localFolder, FileName), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

// Size: OneDrive yedek dosyasını tam indirmiş mi kontrolü (işaret dosyası küçük, önce iner).
public sealed record CloudMarker(string File, string Machine, DateTime CreatedUtc, long Size)
{
    public const string FileName = "son-yedek.json";

    public static CloudMarker? Read(string? cloudFolder)
    {
        if (string.IsNullOrWhiteSpace(cloudFolder)) return null;
        try
        {
            var path = Path.Combine(cloudFolder, FileName);
            return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<CloudMarker>(System.IO.File.ReadAllText(path)) : null;
        }
        catch { return null; }
    }

    public void Write(string cloudFolder)
    {
        var path = Path.Combine(cloudFolder, FileName);
        System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(path + ".tmp", path, overwrite: true);
    }
}
