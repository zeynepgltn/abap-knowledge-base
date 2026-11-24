using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Controllers;

// Bir kategorinin alt kategorileri: oluşturma, yeniden adlandırma, silme ve notları yerleştirme tek ekranda.
// Kullanıcı "Kaydet" demeden hiçbir şey yazılmaz. Seçim değerleri: "" = Genel, "id:5" = var olan alt kategori.
public class SubcategoriesController(NotesDbContext db) : Controller {
 public sealed record SubRow(int Id, string Name, int Count);
 public sealed record NoteRow(int Id, string Title, string Selected);
 public sealed class Page {
  public Category Category { get; init; } = null!;
  public List<SubRow> Existing { get; init; } = [];
  public List<NoteRow> Notes { get; init; } = [];
 }

 [HttpGet("/Subcategories/{id:int}")]
 public async Task<IActionResult> Index(int id) {
  var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
  if (category is null) return NotFound();
  var subs = await db.Subcategories.Where(s => s.CategoryId == id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
   .Select(s => new SubRow(s.Id, s.Name, s.Notes.Count)).ToListAsync();
  var notes = await db.Notes.Where(n => n.CategoryId == id).OrderBy(n => n.SortOrder).ThenBy(n => n.Id)
   .Select(n => new { n.Id, n.Title, n.SubcategoryId }).ToListAsync();
  var rows = notes.Select(n => new NoteRow(n.Id, n.Title, n.SubcategoryId is int s ? $"id:{s}" : "")).ToList();
  // Satırlar seçili gruba göre dizilir (Genel, var olanlar); aynı gruba gidenler alt alta okunur.
  var groupOrder = new List<string> { "" };
  groupOrder.AddRange(subs.Select(s => $"id:{s.Id}"));
  rows = rows.OrderBy(r => groupOrder.IndexOf(r.Selected)).ToList();
  return View(new Page { Category = category, Existing = subs, Notes = rows });
 }

 [HttpPost("/Subcategories/{id:int}")][ValidateAntiForgeryToken]
 public async Task<IActionResult> Save(int id, int[]? subId, string[]? subName, int[]? subDelete, string[]? newName,
   int[]? noteId, string[]? noteSub) {
  var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
  if (category is null) return NotFound();
  var subs = await db.Subcategories.Where(s => s.CategoryId == id).ToListAsync();
  var errors = new List<string>();
  string Clean(string? s) => (s ?? "").Trim();

  // 1) Var olanlar: yeniden adlandır / sil.
  var deleted = (subDelete ?? []).ToHashSet();
  for (var i = 0; i < (subId?.Length ?? 0); i++) {
   var sub = subs.FirstOrDefault(s => s.Id == subId![i]); if (sub is null) continue;
   if (deleted.Contains(sub.Id)) continue;
   var name = Clean(subName?.ElementAtOrDefault(i));
   if (name.Length is 0 or > 80) errors.Add($"\"{sub.Name}\" için 1-80 karakterlik bir ad gir.");
   else sub.Name = name;
   sub.SortOrder = i + 1;
  }
  var keep = subs.Where(s => !deleted.Contains(s.Id)).ToList();

  // 2) Elle yazılan yeni adlar.
  var picks = (noteSub ?? []).Select(Clean).ToList();
  var wanted = (newName ?? []).Select(Clean).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  var nextOrder = keep.Count == 0 ? 1 : keep.Max(s => s.SortOrder) + 1;
  foreach (var name in wanted) {
   if (name.Length > 80) { errors.Add($"\"{name[..20]}…\" adı 80 karakterden uzun."); continue; }
   if (name.Equals(Subcategory.General, StringComparison.OrdinalIgnoreCase)) continue; // Genel = alt kategori yok
   var same = keep.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
   if (same is not null) continue;
   var sub = new Subcategory { CategoryId = id, Name = name, SortOrder = nextOrder++ };
   db.Subcategories.Add(sub); keep.Add(sub);
  }
  if (keep.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
   errors.Add("İki alt kategori aynı adı taşıyamaz.");
  if (errors.Count > 0) { TempData["Flash"] = string.Join(" ", errors); TempData["FlashType"] = "E"; return RedirectToAction(nameof(Index), new { id }); }

  // 3) Notları yerleştir. Silinen alt kategorideki notlar Genel'e düşer.
  var notes = await db.Notes.Where(n => n.CategoryId == id).ToDictionaryAsync(n => n.Id);
  foreach (var n in notes.Values) if (n.SubcategoryId is int s && deleted.Contains(s)) n.SubcategoryId = null;
  var moved = 0;
  for (var i = 0; i < (noteId?.Length ?? 0); i++) {
   if (!notes.TryGetValue(noteId![i], out var note)) continue;
   var pick = picks.ElementAtOrDefault(i) ?? "";
   Subcategory? target = pick.StartsWith("id:") && int.TryParse(pick[3..], out var sid) ? keep.FirstOrDefault(s => s.Id == sid) : null;
   if (target is null ? note.SubcategoryId is null : note.SubcategoryId == target.Id) continue;
   note.SubcategoryId = target?.Id;
   moved++;
  }
  db.Subcategories.RemoveRange(subs.Where(s => deleted.Contains(s.Id)));
  await db.SaveChangesAsync();
  TempData["Flash"] = $"{category.Name}: {keep.Count} alt kategori, {moved} not yerleştirildi.";
  return RedirectToAction("Index", "Notes", new { category = id });
 }

 // Sayfa listesinde sürükle-bırak: not başka bir alt kategori başlığının altına bırakılınca.
 public sealed record MoveRequest(int Id, int? SubcategoryId);
 [HttpPost("/Subcategories/Move")][ValidateAntiForgeryToken]
 public async Task<IActionResult> Move([FromBody] MoveRequest req) {
  var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == req.Id);
  if (note is null) return NotFound();
  if (req.SubcategoryId is int s && !await db.Subcategories.AnyAsync(x => x.Id == s && x.CategoryId == note.CategoryId)) return BadRequest();
  note.SubcategoryId = req.SubcategoryId;
  await db.SaveChangesAsync();
  return NoContent();
 }
}
