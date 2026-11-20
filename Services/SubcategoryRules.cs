using System.Text.RegularExpressions;
using ABAPKnowledgeBase.Data;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Not başlığına bakarak alt kategori önerir. Sadece öneridir: "Alt kategoriler" ekranında kullanıcı görür,
/// değiştirir ve onaylarsa kaydedilir. Kurallar sırayla denenir, ilk uyan kazanır; hiçbiri uymazsa not "Genel"de kalır.
/// Desenler katlanmış (küçük harf, İ/I/ı → i) başlığa uygulanır; bu yüzden desende "ı" yerine "i" yazılır.
/// Yeni kural eklemek için ilgili kategorinin listesine (ad, desen) satırı eklemek yeter.
/// </summary>
public static class SubcategoryRules {
 private static readonly Dictionary<string, (string Name, Regex Pattern)[]> Rules = new(StringComparer.OrdinalIgnoreCase) {
  ["ALV ve Ekran İşlemleri"] = [
   ("CL GUI ALV (OO)", R(@"oo alv|cl_gui|cl gui|tree|nesneleri silme")),
   ("REUSE ALV", R(@"reuse(?!_alv_fieldcatalog)|slis|list display")),
   ("SALV", R(@"salv|hizli alv")),
   ("Alan kataloğu", R(@"fcat|fieldcat|ref_table|ref_field")),
   ("Ekran (Dynpro)", R(@"screen|dynp|gui status|buton|button|call transaction|list-processing|f4")),
  ],
  ["ABAP Temelleri"] = [
   ("Örnek programlar", R(@"rapor|program|servis")),
   ("Mesaj ve metinler", R(@"message|text symbol|condense|bo[sş]luk|escape")),
   ("Tarih işlemleri", R(@"date|tarih|ayin")),
   ("Program akışı", R(@"loop|event|at new|parametre|varyant|status|random")),
  ],
  ["Internal Table ve Modern ABAP"] = [
   ("7.40 sözdizimi", R(@"7\.40|reduce|value #|\bfor\b|kiyaslama")),
   ("Field-symbol ve RTTI", R(@"pointer|assign|rtti|type any")),
   ("Internal table", R(@"read table|tables|structure|geleneksel")),
  ],
  ["SQL, CDS ve Dictionary"] = [
   ("Bakım tablosu", R(@"bak[iı]m")),
   ("Search help", R(@"search help|dynp_values")),
   ("View ve CDS", R(@"\bview\b|cds")),
   ("Open SQL", R(@"sql|join|for all entries|endselect|collect|correspo")),
   ("Dictionary", R(@"append|lock object|deep table|foreign key|ek alan|types")),
  ],
  ["Nesne Yönelimli ABAP"] = [
   ("Tasarım kalıpları", R(@"singleton")),
   ("Kalıtım ve arayüz", R(@"inheritance|interface|abstract|polymorph|cast|constructor")),
   ("Sınıf temelleri", R(@"oop|neden|class")),
  ],
  ["Fonksiyonlar, BAPI ve Entegrasyon"] = [
   ("RFC", R(@"rfc|sm59")),
   ("BAPI", R(@"bapi")),
   ("Fonksiyon modülleri", R(@"fonksiyon|clear|conversion")),
  ],
  ["Forms ve Yazdırma"] = [
   ("Adobe Forms", R(@"adobe|interface-form|formcalc|javascript|binding|sfp|\bjob\b")),
   ("NACE ve çıktı", R(@"nace|nast|driver|entry|mod[uü]lc|abap kismimiz|[oö]zetle")),
   ("Smartforms", R(@"smart|logo|barkod|include|page olu|formun fonksiyon|[cç]ikti")),
  ],
  ["Excel, Dosya ve Mail"] = [
   ("Mail", R(@"mail|kullanici alma")),
   ("Excel ve dosya", R(@"excel|xls|p_file|dosya|template|oaer")),
  ],
  ["Debug ve Hatalar"] = [
   ("Exception", R(@"try|raise|exception")),
   ("Debug ve izleme", R(@"debug|nast|sm|ge[cç]en yol")),
  ],
  ["SAP Araçları ve Transport"] = [
   ("Transport ve request", R(@"request|reguest|task|ta[sş]ima|birle[sş]tirme|release|hatalar")),
   ("abapGit", R(@"abapgit|repo|kurulum|kullanim ak|faydali kaynak")),
   ("Enhancement", R(@"badi|exit|\benc\b|standart")),
   ("İşlem kodları", R(@"i[sş]lem kod|transaction")),
  ],
  ["Projeler"] = [
   ("PP", R(@"\b[zy]?ppds?\d|^pp\b|bom|[uü]retim")),
   ("QM", R(@"\bzqm|^qm\b|kullanim karar")),
   ("MM", R(@"\bzmm|^mm\b|mm-|mal giri")),
   ("SD", R(@"\bzsd|^sd\d*\b|^sd-|sat[iı][sş]")),
   ("FI / CO", R(@"\bz(fi|co)\d|^(fi|co)$")),
   ("EWM", R(@"\bzewm")),
   ("PLM", R(@"\bzplm")),
  ],
 };

 private static Regex R(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

 /// <summary>Kategori için önerilen alt kategori adları, kural sırasıyla.</summary>
 public static IReadOnlyList<string> Names(string categoryName) =>
  Rules.TryGetValue(categoryName, out var rules) ? rules.Select(r => r.Name).ToList() : [];

 /// <summary>Başlığa uyan ilk alt kategori adı; uymazsa null (Genel).</summary>
 public static string? Suggest(string categoryName, string title) {
  if (!Rules.TryGetValue(categoryName, out var rules)) return null;
  var folded = TextMatch.Fold(title.Trim());
  foreach (var (name, pattern) in rules)
   try { if (pattern.IsMatch(folded)) return name; } catch (RegexMatchTimeoutException) { }
  return null;
 }
}
