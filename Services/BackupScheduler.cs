using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ABAPKnowledgeBase.Services;

// Site açıkken yedeklerin zamanlaması:
// - Açılışta: bugünlük yedek yoksa (ya da yedeklenmemiş değişiklik kaldıysa) alınır.
// - Not kaydedilince: son kayıttan 1 dk sonra yedek alınır (art arda kayıtlar tek yedek olur).
// - Kapanışta (Ctrl+C): yedeklenmemiş değişiklik varsa son bir yedek alınır.
// Böylece diğer bilgisayara geçtiğinde OneDrive'da en güncel notlar olur.
public sealed class BackupScheduler(IConfiguration config, IWebHostEnvironment env) : BackgroundService
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _signal = new(0);

    private void OnChanged() => _signal.Release();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BackupService.Changed += OnChanged;
        try
        {
            await Task.Yield(); // sitenin açılmasını bekletme
            await BackupService.RunDailyBackupIfNeededAsync(config, env.ContentRootPath);

            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(stoppingToken);
                // Sessizlik bekle: 1 dk içinde yeni kayıt gelirse süre baştan başlar.
                while (await _signal.WaitAsync(Quiet, stoppingToken)) { }
                await BackupService.RunDailyBackupIfNeededAsync(config, env.ContentRootPath, force: true);
            }
        }
        catch (OperationCanceledException) { /* kapanıyor */ }
        finally
        {
            BackupService.Changed -= OnChanged;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (SyncState.Load(BackupService.LocalFolder(config, env.ContentRootPath)).Dirty)
        {
            Console.WriteLine("Kapanmadan önce son değişiklikler yedekleniyor, pencereyi kapatma...");
            await BackupService.RunDailyBackupIfNeededAsync(config, env.ContentRootPath, force: true);
        }
    }
}

// Veritabanına her başarılı kayıtta (not ekleme/düzenleme/silme/sıralama) BackupService'e haber verir.
public sealed class ChangeTrackingInterceptor(IConfiguration config, IWebHostEnvironment env) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (result > 0) BackupService.NotifyChanged(config, env.ContentRootPath);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (result > 0) BackupService.NotifyChanged(config, env.ContentRootPath);
        return ValueTask.FromResult(result);
    }
}
