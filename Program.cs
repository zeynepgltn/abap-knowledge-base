using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Services;
using Microsoft.EntityFrameworkCore;
using ABAPKnowledgeBase.ImportData;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<NotesDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<ClassificationService>();
var app = builder.Build();

if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
// 404 gibi gövdesiz hata kodlarında boş beyaz sayfa yerine uygulamanın hata sayfası gösterilir.
app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting();
app.MapControllerRoute(name:"default",pattern:"{controller=Notes}/{action=Index}/{id?}");
using (var scope = app.Services.CreateScope()) {
  var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
  // Şema migration'larla güncellenir (Data/Migrations); veriler korunur.
  db.Database.Migrate();
  var missing = CategorySeed.All.Except(db.Categories.Select(c => c.Name).ToList()).ToList();
  if (missing.Count > 0) { db.Categories.AddRange(missing.Select(x => new Category { Name = x })); db.SaveChanges(); }
}

if (args.Contains("--import-notes"))
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<NotesDbContext>();

    await PdfNotesImporter.RunAsync(
        db,
        Path.Combine(
            app.Environment.ContentRootPath,
            "ImportData",
            "notlar.json"
        )
    );

    return;
}

// OneNote aktarımı: --import-onenote önizleme yazar, --apply ile notlar yedeklenip veritabanı OneNote'tan yeniden kurulur.
if (args.Contains("--import-onenote"))
{
    using var scope = app.Services.CreateScope();
    await OneNoteImporter.RunAsync(
        scope.ServiceProvider.GetRequiredService<NotesDbContext>(),
        scope.ServiceProvider.GetRequiredService<ClassificationService>(),
        Path.Combine(app.Environment.ContentRootPath, "ImportData"),
        apply: args.Contains("--apply"));
    return;
}

app.Run();
