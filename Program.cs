using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Services;
using Microsoft.EntityFrameworkCore;
using ABAPKnowledgeBase.ImportData;

var builder = WebApplication.CreateBuilder(args);
// Makineye özel ayarlar (ör. farklı veritabanı yolu): git'e girmez, her ortamda okunur.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Services.AddControllersWithViews();
// Veritabanı: SQLite, %LOCALAPPDATA%\ABAPKnowledgeBase\abap-notlar.db (bkz. Data/SqliteSetup.cs).
// Her kayıt yedekleyiciye haber verir; site açıkken yedekler BackupScheduler ile alınır.
builder.Services.AddSingleton<ChangeTrackingInterceptor>();
builder.Services.AddSingleton<SqliteFunctionsInterceptor>();
builder.Services.AddDbContext<NotesDbContext>((sp, options) => options
    .UseSqlite(SqliteSetup.ConnectionString(builder.Configuration))
    .AddInterceptors(sp.GetRequiredService<ChangeTrackingInterceptor>(), sp.GetRequiredService<SqliteFunctionsInterceptor>()));
builder.Services.AddHostedService<BackupScheduler>();
// Kapanışta (Ctrl+C) son yedeğin bitmesine zaman tanı.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMinutes(2));
builder.Services.AddSingleton<ClassificationService>();
builder.Services.AddScoped<QuizService>();
builder.Services.AddScoped<SessionInfo>();
builder.Services.AddSingleton<KeyTermsRunner>();
var app = builder.Build();

if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
// 404 gibi gövdesiz hata kodlarında boş beyaz sayfa yerine uygulamanın hata sayfası gösterilir.
app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting();
app.MapControllerRoute(name:"default",pattern:"{controller=Notes}/{action=Index}/{id?}");
var root = app.Environment.ContentRootPath;

// Geri yükleme veritabanına hiç bağlanmadan önce yapılır (dosya değiştirilir).
// --restore-from-cloud: OneDrive'daki son yedek, --restore <yol>: belirli bir günün yedeği.
if (args.Contains("--restore") || args.Contains("--restore-from-cloud"))
{
    string? path;
    if (args.Contains("--restore-from-cloud"))
    {
        path = RestoreService.FindLatestCloudBackup(app.Configuration);
        if (path is null) Console.WriteLine("OneDrive'da yedek bulunamadı.");
    }
    else
    {
        var idx = Array.IndexOf(args, "--restore");
        path = idx + 1 < args.Length && !args[idx + 1].StartsWith("--") ? args[idx + 1] : null;
    }
    if (await RestoreService.RunRestoreAsync(app.Configuration, root, path, fromCloud: args.Contains("--restore-from-cloud")))
        Console.WriteLine("Şimdi `dotnet run` ile siteyi aç.");
    return;
}

// İki bilgisayar: diğer bilgisayar OneDrive'a daha yeni yedek bıraktıysa site açılmadan önce buraya alınır.
// İki tarafta da yedeklenmemiş değişiklik varsa hiçbirini ezmeden durur (--keep-local / --restore-from-cloud ile seçilir).
var toolMode = args.Any(a => a is "--backup" or "--import-onenote" or "--import-sqlserver" or "--quiz-onizleme");
if (!toolMode && !args.Contains("--keep-local")
    && !await RestoreService.SyncFromCloudIfNewerAsync(app.Configuration, root))
    return;

int noteCount;
using (var scope = app.Services.CreateScope()) {
  var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
  // Şema migration'larla güncellenir (Data/Migrations); veriler korunur.
  db.Database.Migrate();
  var missing = CategorySeed.All.Except(db.Categories.Select(c => c.Name).ToList()).ToList();
  if (missing.Count > 0) { db.Categories.AddRange(missing.Select(x => new Category { Name = x })); db.SaveChanges(); }
  noteCount = db.Notes.Count();
}

// Tek seferlik geçiş: SQLite boşsa ve bu bilgisayarda eski SQL Server (SQLEXPRESS) veritabanı varsa notlar
// otomatik olarak SQLite'a kopyalanır ve hemen OneDrive'a yedeklenir. --import-sqlserver ile elle de çalışır.
var sqlServer = app.Configuration.GetConnectionString("SqlServer");
if (args.Contains("--import-sqlserver") || (!toolMode && noteCount == 0 && !string.IsNullOrWhiteSpace(sqlServer)))
{
    using var scope = app.Services.CreateScope();
    var imported = await SqlServerImporter.RunAsync(scope.ServiceProvider.GetRequiredService<NotesDbContext>(), sqlServer, quiet: !args.Contains("--import-sqlserver"));
    if (imported) await BackupService.RunDailyBackupIfNeededAsync(app.Configuration, root, force: true);
    if (args.Contains("--import-sqlserver")) return;
}

// --backup: hemen yedek alır (OneDrive'a da kopyalar) ve siteyi açmadan çıkar.
if (args.Contains("--backup"))
{
    await BackupService.RunDailyBackupIfNeededAsync(app.Configuration, root, force: true);
    return;
}

// OneNote aktarımı: --import-onenote önizleme yazar, --apply ile notlar yedeklenip veritabanı OneNote'tan yeniden kurulur.
// --add ile sadece eksik sayfalar bulunur; --add --apply bunları mevcut notlara dokunmadan ekler.
if (args.Contains("--import-onenote"))
{
    // Notlar artık doğrudan siteye yazılıyorsa appsettings.json → "OneNote": { "ImportEnabled": false }
    // ile aktarım tamamen kapatılır; yanlışlıkla çalıştırılsa bile hiçbir şey yapmaz.
    if (!(app.Configuration.GetValue<bool?>("OneNote:ImportEnabled") ?? true))
    {
        Console.WriteLine("OneNote aktarımı kapalı (OneNote:ImportEnabled=false). Notlar doğrudan siteye yazılıyor.");
        return;
    }
    // Veritabanına yazan her aktarımdan önce tam yedek alınır.
    if (args.Contains("--apply"))
        await BackupService.RunDailyBackupIfNeededAsync(app.Configuration, root, force: true);
    using var scope = app.Services.CreateScope();
    await OneNoteImporter.RunAsync(
        scope.ServiceProvider.GetRequiredService<NotesDbContext>(),
        scope.ServiceProvider.GetRequiredService<ClassificationService>(),
        Path.Combine(root, "ImportData"),
        apply: args.Contains("--apply"),
        add: args.Contains("--add"),
        force: args.Contains("--force"),
        updateChanged: args.Contains("--update-changed"));
    return;
}

// --quiz-onizleme [N]: kaydetmeden N günlük örnek quiz yazar (soru kalitesini görmek için).
if (args.Contains("--quiz-onizleme"))
{
    var idx = Array.IndexOf(args, "--quiz-onizleme");
    var days = idx + 1 < args.Length && int.TryParse(args[idx + 1], out var n) ? n : 1;
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    using var scope = app.Services.CreateScope();
    var quiz = scope.ServiceProvider.GetRequiredService<QuizService>();
    var pool = await quiz.LoadPoolAsync();
    Console.WriteLine($"Quiz havuzu: {pool.Count} not, {pool.Sum(m => m.Concepts.Count())} kavram, {pool.Sum(m => m.CodePairs.Count())} açıklamalı kod satırı, {pool.Sum(m => m.CodeBlocks.Count)} kod bloğu.");
    for (var d = 0; d < days; d++)
    {
        var date = QuizService.Today.AddDays(d);
        Console.WriteLine($"\n======== {date:dd.MM.yyyy} ========");
        foreach (var q in await quiz.PreviewAsync(date, date.DayNumber))
        {
            Console.WriteLine($"\n[{q.Order}] {q.Kind} · {q.Difficulty} · {q.Type} · not #{q.NoteId}");
            Console.WriteLine(q.Prompt);
            if (q.Code is not null) Console.WriteLine("  " + q.Code.Replace("\n", "\n  "));
            var choices = q.Choices;
            for (var i = 0; i < choices.Count; i++) Console.WriteLine($"  {(i == q.CorrectIndex ? "*" : " ")} {(char)('A' + i)}) {choices[i]}");
            Console.WriteLine($"  → {q.Explanation}");
        }
    }
    return;
}

// Yedekler BackupScheduler'da: açılışta günlük, not kaydedilince 1 dk sonra, kapanışta (Ctrl+C) son hâl.
app.Run();
