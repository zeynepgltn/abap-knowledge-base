using ABAPKnowledgeBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ABAPKnowledgeBase.Services;

// İşlem kodları: SAP'deki gibi komut alanına kod yazıp Enter'a basınca ilgili ekrana gidilir.
// Her kategorinin bir Z-kodu var (müşteriye özel işlemler SAP'de Z ile başlar); ayrıca sabit komutlar.
// "/n" öneki SAP'deki gibi kabul edilir: "/nZALV" = "ZALV". Kod olmayan her şey arama olarak çalışır.
public static class TCodes {
 public const string Home = "ZNOT", Quiz = "ZQUIZ", NewNote = "ZYENI", Dump = "ST22";

 private static readonly Dictionary<string, string> ByCategory = new(StringComparer.OrdinalIgnoreCase) {
  ["ABAP Temelleri"] = "ZTEMEL",
  ["Internal Table ve Modern ABAP"] = "ZITAB",
  ["SQL, CDS ve Dictionary"] = "ZSQL",
  ["Nesne Yönelimli ABAP"] = "ZOO",
  ["ALV ve Ekran İşlemleri"] = "ZALV",
  ["Fonksiyonlar, BAPI ve Entegrasyon"] = "ZBAPI",
  ["Forms ve Yazdırma"] = "ZFORM",
  ["Excel, Dosya ve Mail"] = "ZDOSYA",
  ["Debug ve Hatalar"] = "ZDEBUG",
  ["SAP Araçları ve Transport"] = "ZARAC",
  [CategorySeed.Projects] = "ZPROJE",
 };

 // Listede olmayan (sonradan eklenmiş) kategori için adın ilk harflerinden kod üretilir.
 public static string ForCategory(string name) {
  if (ByCategory.TryGetValue(name, out var code)) return code;
  var letters = new string(TextMatch.Fold(name).ToUpperInvariant().Where(char.IsAsciiLetter).Take(5).ToArray());
  return "Z" + (letters.Length > 0 ? letters : "KAT");
 }

 // Komutu adrese ve durum çubuğu mesajına çevirir; kod değilse null (arama olarak devam edilir).
 public static (string Url, string Message)? Resolve(string input, IEnumerable<(int Id, string Name)> categories) {
  var cmd = input.Trim();
  var prefixed = cmd.StartsWith("/n", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("/o", StringComparison.OrdinalIgnoreCase);
  if (prefixed) cmd = cmd[2..].Trim();
  if (cmd.Length == 0) return prefixed ? ("/", $"{Home}: Notlarım açıldı.") : null;
  if (cmd.Contains(' ')) return null;
  if (cmd.Equals(Home, StringComparison.OrdinalIgnoreCase)) return ("/", $"{Home}: Notlarım açıldı.");
  if (cmd.Equals(Quiz, StringComparison.OrdinalIgnoreCase)) return ("/Quiz", $"{Quiz}: Günün quiz'i açıldı.");
  if (cmd.Equals(NewNote, StringComparison.OrdinalIgnoreCase)) return ("/Notes/Create", $"{NewNote}: Yeni not ekranı açıldı.");
  foreach (var (id, name) in categories) {
   var code = ForCategory(name);
   if (cmd.Equals(code, StringComparison.OrdinalIgnoreCase)) return ($"/?category={id}", $"{code}: {name} açıldı.");
  }
  return null;
 }

 // Z ile başlayan ama tanınmayan tek kelime: SAP'deki "işlem kodu yok" uyarısı, ardından arama yapılır.
 public static bool LooksLikeCode(string input) {
  var cmd = input.Trim();
  if (cmd.StartsWith("/n", StringComparison.OrdinalIgnoreCase)) return true;
  return cmd.Length is >= 3 and <= 12 && (cmd[0] is 'Z' or 'z') && cmd.All(char.IsAsciiLetterOrDigit) && cmd.Any(char.IsAsciiLetterUpper);
 }
}

// Her sayfanın başlığındaki komut alanı ve alttaki durum çubuğu için kategori listesi ve sayılar.
// İstek başına bir kez okunur; veritabanına ulaşılamazsa (ör. hata sayfası) boş döner.
public sealed class SessionInfo(NotesDbContext db, ILogger<SessionInfo> log) {
 public sealed record Category(int Id, string Name, string Code, int NoteCount);
 private List<Category>? _categories;

 public async Task<List<Category>> CategoriesAsync() {
  if (_categories is not null) return _categories;
  try {
   var rows = await db.Categories.AsNoTracking().OrderBy(c => c.Id)
    .Select(c => new { c.Id, c.Name, Count = c.Notes.Count }).ToListAsync();
   _categories = rows.Select(r => new Category(r.Id, r.Name, TCodes.ForCategory(r.Name), r.Count)).ToList();
  } catch (Exception ex) {
   log.LogWarning(ex, "Komut alanı için kategoriler okunamadı.");
   _categories = [];
  }
  return _categories;
 }
}
