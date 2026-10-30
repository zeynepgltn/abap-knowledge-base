using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using ABAPKnowledgeBase.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Controllers;
public class NotesController(NotesDbContext db, ClassificationService classifier) : Controller {
 // edit=true: seçili not sağ panelde düzenleme formuyla açılır.
 // company: Projeler kategorisinde sadece o firmanın sayfaları listelenir.
 public async Task<IActionResult> Index(string? q, int? category, int? note, int? company, bool edit = false) {
  ViewBag.Edit = edit;
  if (edit) await LoadCategories();
  // category=0 "kategori yok" demektir; filtre ve görünüm aynı değeri kullansın.
  if (category is <= 0) category = null;
  ViewBag.SelectedNote = note;
  ViewBag.Query = q;
  ViewBag.SelectedCategory = category;
  // Count notes in every category, including empty categories.
  var categories = await db.Categories.OrderBy(c => c.Id)
   .Select(c => new CategoryOverview { Id = c.Id, Name = c.Name, NoteCount = c.Notes.Count })
   .ToListAsync();
  ViewBag.CategoryOverviews = categories;
  ViewBag.TotalNotes = categories.Sum(c => c.NoteCount);
  ViewBag.CategoryName = categories.FirstOrDefault(c => c.Id == category)?.Name;
  // Var olmayan kategori Id'si boş bir "Arama sonuçları" sayfası yerine 404 verir.
  if (category.HasValue && ViewBag.CategoryName is null) return NotFound();
  var projectsId = categories.FirstOrDefault(c => c.Name == CategorySeed.Projects)?.Id;
  ViewBag.ProjectsCategoryId = projectsId;
  ViewBag.Companies = await db.Companies.OrderBy(c => c.Name)
   .Select(c => new CategoryOverview { Id = c.Id, Name = c.Name, NoteCount = c.Notes.Count }).ToListAsync();
  // Firma sadece Projeler kategorisinde anlamlıdır.
  if (category != projectsId) company = null;
  ViewBag.SelectedCompany = company;
  ViewBag.CompanyName = company is null ? null : (ViewBag.Companies as List<CategoryOverview>)!.FirstOrDefault(c => c.Id == company)?.Name;
  if (company.HasValue && ViewBag.CompanyName is null) return NotFound();
  var notes = db.Notes.Include(n => n.Category).Include(n => n.Company).AsQueryable();
  if (category.HasValue) notes = notes.Where(n => n.CategoryId == category.Value);
  if (company.HasValue) notes = notes.Where(n => n.CompanyId == company.Value);
  if (!string.IsNullOrWhiteSpace(q)) {
   var term = q.Trim();
   notes = notes.Where(n => n.Title.Contains(term) || n.Body.Contains(term) ||
       (n.Code != null && n.Code.Contains(term)) || (n.Tags != null && n.Tags.Contains(term)) ||
       n.Images.Any(i => i.OcrText != null && i.OcrText.Contains(term)));
  }
  if (!category.HasValue && string.IsNullOrWhiteSpace(q)) return View(new List<Note>());
  // Kategori listesi elle verilen sırada gösterilir ve sürüklenerek sıralanabilir; arama sonuçları en yeni üstte.
  var searching = !string.IsNullOrWhiteSpace(q);
  ViewBag.CanReorder = !searching;
  notes = searching ? notes.OrderByDescending(n => n.UpdatedAt) : notes.OrderBy(n => n.SortOrder).ThenBy(n => n.Id);
  return View(await notes.Take(searching ? 200 : 1000).ToListAsync());
 }
 public async Task<IActionResult> Details(int id) {
  var note=await db.Notes.Include(n=>n.Category).Include(n=>n.Company).FirstOrDefaultAsync(n=>n.Id==id);
  return note is null?NotFound():View(note);
 }
 // Not gövdesindeki "[resim:N]" satırlarının gösterdiği resim.
 [ResponseCache(Duration = 86400)]
 public async Task<IActionResult> Image(int noteId, int order) {
  var image = await db.NoteImages.AsNoTracking().FirstOrDefaultAsync(i => i.NoteId == noteId && i.Order == order);
  return image is null ? NotFound() : File(image.Data, image.ContentType);
 }
 // category/company verilirse form o kategori ve firma seçili açılır (ör. bir firmanın sayfalarındayken "+ Yeni Not").
 public async Task<IActionResult> Create(int? category, int? company) {
  await LoadCategories();
  return View(new Note { CategoryId = category ?? 0, CompanyId = company });
 }
 [HttpPost][ValidateAntiForgeryToken]
 // Sadece formdaki alanlar bağlanır: Id, CreatedAt gibi alanlar dışarıdan gönderilse bile yok sayılır.
 public async Task<IActionResult> Create([Bind(nameof(Note.Title), nameof(Note.Body), nameof(Note.Code), nameof(Note.Tags),
   nameof(Note.CategoryId), nameof(Note.CompanyId), nameof(Note.SourceFile), nameof(Note.SourcePage))] Note note) {
  if (!await db.Categories.AnyAsync(c=>c.Id==note.CategoryId)) ModelState.AddModelError(nameof(note.CategoryId),"Kategori seçmelisin");
  await CheckCompany(note);
  if (!ModelState.IsValid) { await LoadCategories(); return View(note); }
  note.CreatedAt=note.UpdatedAt=DateTime.UtcNow;
  note.SortOrder=await NextSortOrder(note.CategoryId);
  db.Notes.Add(note);await db.SaveChangesAsync();return RedirectToAction(nameof(Details),new{id=note.Id});
 }
 public async Task<IActionResult> Edit(int id) { var note=await db.Notes.FindAsync(id); if(note is null)return NotFound(); await LoadCategories(); return View(note); }
 [HttpPost][ValidateAntiForgeryToken]
 // inline=true: form okuma ekranının sağ panelinden geldi; kayıttan sonra aynı listeye ve nota dönülür.
 public async Task<IActionResult> Edit(int id,Note input,bool inline=false,int? returnCategory=null,string? returnQ=null,int? returnCompany=null) {
  if(id!=input.Id)return BadRequest();
  if(!await db.Categories.AnyAsync(c=>c.Id==input.CategoryId))ModelState.AddModelError(nameof(input.CategoryId),"Kategori seçmelisin");
  await CheckCompany(input);
  if(!ModelState.IsValid){
   await LoadCategories();
   // Hata olursa tam sayfa düzenleme gösterilir ama dönüş adresi korunur.
   if(inline){ViewBag.Inline=true;ViewBag.ReturnCategory=returnCategory;ViewBag.ReturnQ=returnQ;ViewBag.ReturnCompany=returnCompany;
    ViewBag.CancelUrl=Url.Action(nameof(Index),new{category=returnCategory,q=returnQ,company=returnCompany,note=id});}
   return View(input);
  }
  var note=await db.Notes.FindAsync(id);if(note is null)return NotFound();
  // Başka kategoriye taşınan not, yeni kategorisinin sonuna eklenir.
  if(note.CategoryId!=input.CategoryId)note.SortOrder=await NextSortOrder(input.CategoryId);
  note.Title=input.Title;note.Body=input.Body;note.Code=input.Code;note.Tags=input.Tags;note.CategoryId=input.CategoryId;note.CompanyId=input.CompanyId;
  note.SourceFile=input.SourceFile;note.SourcePage=input.SourcePage;note.UpdatedAt=DateTime.UtcNow;
  await db.SaveChangesAsync();
  if(inline){
   TempData["Flash"]=$"\"{note.Title}\" kaydedildi.";
   // Not başka bir firmaya/kategoriye taşındıysa listede kaybolmasın diye firma filtresi bırakılır.
   if(returnCompany is not null && returnCompany!=note.CompanyId)returnCompany=null;
   return RedirectToAction(nameof(Index),new{category=returnCategory,q=returnQ,company=returnCompany,note=id});
  }
  return RedirectToAction(nameof(Details),new{id});
 }
 // Silme sadece POST + anti-forgery ile yapılır; GET linkiyle (ör. tarayıcı ön yüklemesi) not silinemez.
 // q/category/company gelirse kullanıcı geldiği listeye döner; gelmezse silinen notun kategorisine döner.
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Delete(int id, string? q, int? category, int? company) {
  var note = await db.Notes.FindAsync(id);
  if (note is null) return NotFound();
  db.Notes.Remove(note);
  await db.SaveChangesAsync();
  TempData["Flash"] = $"\"{note.Title}\" silindi.";
  if (category is null && string.IsNullOrWhiteSpace(q)) category = note.CategoryId;
  return RedirectToAction(nameof(Index), new { q, category, company });
 }

 // Sayfa listesinde sürükle-bırak sonrası yeni sıra. ids: listedeki notlar yeni sıralarıyla.
 // Sadece bu notların mevcut sıra numaraları kendi aralarında yeniden dağıtılır; böylece firma
 // filtresiyle görülen alt liste sıralansa da kategorideki diğer notların yeri değişmez.
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> Reorder([FromBody] int[]? ids) {
  if (ids is null || ids.Length == 0 || ids.Distinct().Count() != ids.Length) return BadRequest();
  var notes = await db.Notes.Where(n => ids.Contains(n.Id)).ToListAsync();
  if (notes.Count != ids.Length || notes.Select(n => n.CategoryId).Distinct().Count() != 1) return BadRequest();
  var slots = notes.Select(n => n.SortOrder).OrderBy(x => x).ToList();
  // Aynı sıra numarası olan notlar varsa (eski veriler) çakışmasın diye ardışık numara verilir.
  if (slots.Distinct().Count() != slots.Count) slots = Enumerable.Range(slots[0], slots.Count).ToList();
  var byId = notes.ToDictionary(n => n.Id);
  for (var i = 0; i < ids.Length; i++) byId[ids[i]].SortOrder = slots[i];
  await db.SaveChangesAsync();
  return NoContent();
 }

 // ---------- Firmalar (Projeler kategorisinin alt alanı) ----------

 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> CreateCompany(string? name) {
  name = name?.Trim();
  if (string.IsNullOrEmpty(name) || name.Length > 100) TempData["Flash"] = "Firma adı 1-100 karakter olmalı.";
  else if (await db.Companies.AnyAsync(c => c.Name == name)) TempData["Flash"] = $"\"{name}\" adında bir firma zaten var.";
  else {
   var company = new Company { Name = name };
   db.Companies.Add(company); await db.SaveChangesAsync();
   TempData["Flash"] = $"\"{name}\" firması eklendi.";
   return RedirectToAction(nameof(Index), new { category = await ProjectsId(), company = company.Id });
  }
  return RedirectToAction(nameof(Index), new { category = await ProjectsId() });
 }
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> RenameCompany(int id, string? name) {
  var company = await db.Companies.FindAsync(id);
  if (company is null) return NotFound();
  name = name?.Trim();
  if (string.IsNullOrEmpty(name) || name.Length > 100) TempData["Flash"] = "Firma adı 1-100 karakter olmalı.";
  else if (await db.Companies.AnyAsync(c => c.Name == name && c.Id != id)) TempData["Flash"] = $"\"{name}\" adında bir firma zaten var.";
  else { company.Name = name; await db.SaveChangesAsync(); TempData["Flash"] = $"Firma adı \"{name}\" olarak değiştirildi."; }
  return RedirectToAction(nameof(Index), new { category = await ProjectsId(), company = id });
 }
 // Sayfası olan firma silinmez; önce sayfaları silinmeli ya da başka firmaya taşınmalı.
 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> DeleteCompany(int id) {
  var company = await db.Companies.FindAsync(id);
  if (company is null) return NotFound();
  if (await db.Notes.AnyAsync(n => n.CompanyId == id)) {
   TempData["Flash"] = $"\"{company.Name}\" firmasında hâlâ sayfa var; önce sayfaları sil veya başka firmaya taşı.";
   return RedirectToAction(nameof(Index), new { category = await ProjectsId(), company = id });
  }
  db.Companies.Remove(company); await db.SaveChangesAsync();
  TempData["Flash"] = $"\"{company.Name}\" firması silindi.";
  return RedirectToAction(nameof(Index), new { category = await ProjectsId() });
 }

 [HttpPost][ValidateAntiForgeryToken]
 public IActionResult Suggest([FromBody] SuggestRequest? input) => input is null ? BadRequest() : Json(classifier.Suggest($"{input.Title} {input.Body} {input.Code}").Select(x=>new {category=x.Category,score=x.Score,evidence=x.Evidence}));

 private async Task LoadCategories() {
  ViewBag.CategoryItems=new SelectList(await db.Categories.OrderBy(c=>c.Id).ToListAsync(),"Id","Name");
  ViewBag.CompanyItems=new SelectList(await db.Companies.OrderBy(c=>c.Name).ToListAsync(),"Id","Name");
  ViewBag.ProjectsCategoryId=await ProjectsId();
 }
 private async Task<int> NextSortOrder(int categoryId) => (await db.Notes.Where(n => n.CategoryId == categoryId).MaxAsync(n => (int?)n.SortOrder) ?? 0) + 1;
 private async Task<int?> ProjectsId() => await db.Categories.Where(c => c.Name == CategorySeed.Projects).Select(c => (int?)c.Id).FirstOrDefaultAsync();
 // Firma sadece Projeler kategorisinde tutulur; başka kategoride gelen firma bilgisi atılır.
 private async Task CheckCompany(Note note) {
  if (note.CategoryId != await ProjectsId()) { note.CompanyId = null; return; }
  if (note.CompanyId is int id && !await db.Companies.AnyAsync(c => c.Id == id)) ModelState.AddModelError(nameof(note.CompanyId), "Firma bulunamadı");
 }
 public sealed record SuggestRequest(string? Title,string? Body,string? Code);
 public sealed class CategoryOverview { public int Id {get;set;} public string Name {get;set;}=""; public int NoteCount {get;set;} }
}
