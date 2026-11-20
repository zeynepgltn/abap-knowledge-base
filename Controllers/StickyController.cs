using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Controllers;

public sealed record StickyUpdate(int Id, string? Text, string? Color, bool? Done);

// Yapışkan notlar: her sayfanın sağındaki sütunda gösterilir (StickyDockViewComponent).
// Notlar yazdıkça kaydedilir (site.js → buradaki JSON istekleri).
public class StickyController(NotesDbContext db) : Controller {
 private const int MaxText = 4000;

 // Eski /Sticky sayfası kalktı; notlar artık her sayfanın sağında.
 public IActionResult Index() => RedirectToAction("Index", "Notes");

 // Yeni boş not panonun başına eklenir.
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Create([FromBody] StickyUpdate? input) {
  var first = await db.StickyNotes.MinAsync(s => (int?)s.SortOrder) ?? 1;
  var note = new StickyNote { Color = ValidColor(input?.Color) ?? StickyColors.Yellow, Text = Clip(input?.Text) ?? "", SortOrder = first - 1 };
  db.StickyNotes.Add(note);
  await db.SaveChangesAsync();
  return Json(new { id = note.Id, date = DateLabel(note.CreatedAt) });
 }

 // Sadece gönderilen alanlar değişir (metin, renk veya tamamlandı).
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Update([FromBody] StickyUpdate? input) {
  if (input is null) return BadRequest();
  var note = await db.StickyNotes.FindAsync(input.Id);
  if (note is null) return NotFound();
  if (input.Text is not null) note.Text = Clip(input.Text)!;
  if (input.Color is not null) note.Color = ValidColor(input.Color) ?? note.Color;
  if (input.Done is bool done) note.Done = done;
  note.UpdatedAt = DateTime.UtcNow;
  await db.SaveChangesAsync();
  return NoContent();
 }

 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Delete([FromBody] StickyUpdate? input) {
  if (input is null) return BadRequest();
  await db.StickyNotes.Where(s => s.Id == input.Id).ExecuteDeleteAsync();
  return NoContent();
 }

 // ---------- Resimler ----------
 // Nota yapıştırılan (Ctrl+V), sürüklenen veya 🖼 ile seçilen resim; her resim ayrı istekle gelir.
 private const int MaxImageBytes = 10 * 1024 * 1024;
 private static readonly HashSet<string> ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"];

 [HttpPost][ValidateAntiForgeryToken][RequestSizeLimit(MaxImageBytes + 64 * 1024)][RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 64 * 1024)]
 public async Task<IActionResult> AddImage(int id, IFormFile? file) {
  if (file is null || file.Length == 0) return BadRequest("Resim boş.");
  if (!ImageTypes.Contains(file.ContentType)) return BadRequest("Sadece PNG, JPEG, GIF, WEBP veya BMP resim eklenebilir.");
  if (file.Length > MaxImageBytes) return BadRequest("Resim 10 MB'tan büyük olamaz.");
  var note = await db.StickyNotes.FindAsync(id);
  if (note is null) return NotFound();
  using var ms = new MemoryStream();
  await file.CopyToAsync(ms);
  var image = new StickyImage { StickyNoteId = id, ContentType = file.ContentType, Data = ms.ToArray() };
  db.StickyImages.Add(image);
  note.UpdatedAt = DateTime.UtcNow;
  await db.SaveChangesAsync();
  return Json(new { id = image.Id });
 }

 // Resim içeriği değişmez (yeni resim = yeni id), tarayıcı önbellekte tutabilir.
 [HttpGet]
 public async Task<IActionResult> Image(int id) {
  var image = await db.StickyImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
  if (image is null) return NotFound();
  Response.Headers.CacheControl = "private, max-age=31536000, immutable";
  return File(image.Data, image.ContentType);
 }

 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> DeleteImage([FromBody] StickyUpdate? input) {
  if (input is null) return BadRequest();
  await db.StickyImages.Where(i => i.Id == input.Id).ExecuteDeleteAsync();
  return NoContent();
 }

 // Sürükle-bırak sonrası panodaki yeni sıra (tüm notların id'leri, baştan sona).
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Reorder([FromBody] int[]? ids) {
  if (ids is null || ids.Distinct().Count() != ids.Length) return BadRequest();
  var notes = await db.StickyNotes.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
  for (var i = 0; i < ids.Length; i++) if (notes.TryGetValue(ids[i], out var n)) n.SortOrder = i;
  await db.SaveChangesAsync();
  return NoContent();
 }

 public static string DateLabel(DateTime utc) =>
  utc.ToLocalTime().ToString("d MMM", new System.Globalization.CultureInfo("tr-TR"));
 private static string? ValidColor(string? c) => c is not null && StickyColors.All.Contains(c) ? c : null;
 private static string? Clip(string? s) => s is null ? null : s.Length > MaxText ? s[..MaxText] : s;
}
