using ABAPKnowledgeBase.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.ViewComponents;

// Her sayfanın sağında sabit duran yapışkan notlar sütunu (OneNote'taki yapışkan notlar gibi).
// Veritabanına ulaşılamazsa (ör. hata sayfası) sütun hiç gösterilmez.
public sealed class StickyDockViewComponent(NotesDbContext db, ILogger<StickyDockViewComponent> log) : ViewComponent {
 public async Task<IViewComponentResult> InvokeAsync() {
  try {
   var notes = await db.StickyNotes.AsNoTracking().OrderBy(s => s.Done).ThenBy(s => s.SortOrder).ThenByDescending(s => s.Id).ToListAsync();
   // Resim verisi değil sadece id'ler: resimler <img> ile ayrıca (önbellekten) yüklenir.
   var images = await db.StickyImages.AsNoTracking().OrderBy(i => i.Id).Select(i => new { i.Id, i.StickyNoteId }).ToListAsync();
   foreach (var n in notes) n.ImageIds = images.Where(i => i.StickyNoteId == n.Id).Select(i => i.Id).ToList();
   return View(notes);
  } catch (Exception ex) {
   log.LogWarning(ex, "Yapışkan notlar yüklenemedi.");
   return Content("");
  }
 }
}
