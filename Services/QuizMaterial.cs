using System.Text.RegularExpressions;
using ABAPKnowledgeBase.Models;
namespace ABAPKnowledgeBase.Services;

// Nottan çıkarılan bir "kavram: açıklama" çifti. IsCode: kavram bir kod satırı ("lo_alv->display( ).: ...").
public sealed record QuizPair(string Term, string Definition, bool IsCode);

// Bir notun quiz için kullanılabilecek malzemesi.
public sealed class NoteMaterial {
 public required Note Note {get;init;}
 public required string Category {get;init;}
 public List<QuizPair> Pairs {get;}=[];
 // Kod bloklarının anlamlı satırları (boş ve yorum satırları atılmış).
 public List<List<string>> CodeBlocks {get;}=[];
 // Başlık + düz metin + kod; "bu kavram notta geçiyor mu" kontrolü için.
 public string AllText {get;set;}="";
 public IEnumerable<QuizPair> Concepts => Pairs.Where(p => !p.IsCode);
 public IEnumerable<QuizPair> CodePairs => Pairs.Where(p => p.IsCode);
}

/// <summary>
/// Not gövdesinden soru malzemesi çıkarır (kural tabanlı, makine öğrenmesi yok):
///  • "KAVRAM: açıklama" satırları (madde işaretli veya numaralı olabilir),
///  • "KAVRAM | açıklama" tablo satırları (birden çok satırlık tabloda ilk satır başlık sayılır),
///  • ``` arasındaki kod blokları.
/// Notlar serbest yazıldığı için çıkan çiftlerin bir kısmı gürültülü olabilir; filtreler bunu azaltır,
/// kalan kötü soruları kullanıcının 👎 puanları gösterecek.
/// </summary>
public static partial class QuizMaterial {
 private static readonly System.Globalization.CultureInfo Turkish = new("tr-TR");
 // "Not: ...", "Örnek: ..." gibi kavram olmayan etiketler.
 private static readonly HashSet<string> Labels = new(StringComparer.OrdinalIgnoreCase) {
  "not","notlar","örnek","örneğin","örnekler","dikkat","açıklama","amaç","sonuç","neden","önemli","kullanım","fark","farklar",
  "özet","ipucu","uyarı","adım","adımlar","avantaj","avantajlar","dezavantaj","kısaca","yani","mesela","soru","cevap","kaynak",
  "tarih","link","url","sebep","çözüm","problem","sorun","hata","durum","senaryo","mantık","işlem","yöntem","kod","tanım",
  "anlamı","görev","görevi","ne işe yarar","nedir","nasıl","özellik","özellikler","sonuçlar","çıktı","girdi","tip","türü","örnek kod",
  "avantajları","dezavantajları","nasıl çalışır","kullanımı","içeriği","özellikleri","farkı","amacı","nedeni","notlar","detay","detaylar" };

 [GeneratedRegex(@"^\s*(?:[•\-\*·▪►●○◦]|\d{1,2}[.)])?\s*(?<term>[^:|]{2,70}?)\s*:\s+(?<def>\S.{18,})$")]
 private static partial Regex ColonPair();
 [GeneratedRegex(@"^\s*(?<term>[^|]{1,50}?)\s*\|\s*(?<def>[^|]{2,200}?)\s*$")]
 private static partial Regex PipeRow();
 // ABAP değişken adlandırması: lv_, lo_, gt_, ls_ ...
 [GeneratedRegex(@"\b[a-z]{1,3}_[a-z0-9_]+\b")]
 private static partial Regex AbapVariable();
 // Koddaki metin sabiti: 'X' (Türkçe kesme işaretiyle karışmasın: "SAP'ta" kod değil).
 [GeneratedRegex(@"(?:^|[\s=(,])'")]
 private static partial Regex CodeQuote();
 // Kavram olmayan numaralı başlıklar: "1 Ön Koşul", "Adım 2", "2. Aşama".
 [GeneratedRegex(@"^\d+\s|^(?:adım|aşama|step|bölüm|yöntem|seçenek|yol)\s*\d", RegexOptions.IgnoreCase)]
 private static partial Regex NumberedHeading();
 [GeneratedRegex(@"<[^>]*>|\(cite[^)]*\)?")]
 private static partial Regex Markup();
 [GeneratedRegex(@"\s+")]
 private static partial Regex Spaces();

 public static NoteMaterial Extract(Note note, string category) {
  var m = new NoteMaterial { Note = note, Category = category };
  var all = new List<string> { note.Title };
  foreach (var seg in NoteBody.Parse(note.Body)) {
   if (seg.Kind == NoteBody.Kind.Code) {
    var lines = seg.Text.Split('\n').Select(l => l.TrimEnd()).Where(IsCodeLine).ToList();
    if (lines.Count > 0) m.CodeBlocks.Add(lines);
    all.Add(seg.Text);
   } else if (seg.Kind == NoteBody.Kind.Text) {
    all.Add(seg.Text);
    ReadPairs(seg.Text, m.Pairs);
   }
  }
  if (!string.IsNullOrWhiteSpace(note.Code)) {
   var lines = note.Code.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(IsCodeLine).ToList();
   if (lines.Count > 0) m.CodeBlocks.Add(lines);
   all.Add(note.Code);
  }
  m.AllText = string.Join("\n", all);
  // Aynı kavram notta iki kez tanımlanmışsa ilki kalır.
  var unique = m.Pairs.DistinctBy(p => p.Term, StringComparer.OrdinalIgnoreCase).ToList();
  m.Pairs.Clear(); m.Pairs.AddRange(unique);
  return m;
 }

 private static void ReadPairs(string text, List<QuizPair> pairs) {
  var lines = text.Split('\n');
  for (var i = 0; i < lines.Length; i++) {
   var line = Markup().Replace(lines[i], "").Trim();
   if (line.Length == 0) continue;
   var pipe = PipeRow().Match(line);
   if (pipe.Success) {
    // Tablonun ilk satırı başlıktır ("Fonksiyon | Mantık"): birden çok satırlık tablolarda atlanır.
    var prevIsRow = i > 0 && lines[i - 1].Contains('|');
    var nextIsRow = i + 1 < lines.Length && lines[i + 1].Contains('|');
    if (!prevIsRow && nextIsRow) continue;
    Add(pairs, pipe.Groups["term"].Value, pipe.Groups["def"].Value, minDef: 2);
    continue;
   }
   var colon = ColonPair().Match(line);
   if (colon.Success) Add(pairs, colon.Groups["term"].Value, colon.Groups["def"].Value, minDef: 20);
  }
 }

 private static void Add(List<QuizPair> pairs, string term, string def, int minDef) {
  term = Spaces().Replace(term, " ").Trim().TrimEnd('.', ',', ';').Trim();
  def = Spaces().Replace(def, " ").Trim();
  if (term.Length < 2 || def.Length < minDef || def.Length > 600) return;
  if (term.EndsWith('?') || def.StartsWith("//") || term.Contains("http", StringComparison.OrdinalIgnoreCase)) return;
  var isCode = IsCodeTerm(term);
  if (!isCode) {
   // Kavram kısa olmalı ve cümle ortası olmamalı: büyük harf/rakamla başlar, en fazla 5 kelime,
   // açıklama küçük harfle başlıyorsa önceki cümlenin devamıdır.
   if (Labels.Contains(term.ToLower(Turkish)) || NumberedHeading().IsMatch(term) || term.Split(' ').Length > 5 || term.Length > 50) return;
   if (!char.IsUpper(term[0]) && !char.IsDigit(term[0])) return;
   // Sadece sayı ("130", "10 5") kavram değildir.
   if (!term.Any(char.IsLetter)) return;
   if (minDef > 2 && char.IsLower(def[0])) return;
  }
  pairs.Add(new QuizPair(term, def, isCode));
 }

 // Kavram bir kod satırı mı? (değişken adı, -> / => / ?= / #( / tırnak / atama içeriyor)
 public static bool IsCodeTerm(string term) =>
  term.Contains("->") || term.Contains("=>") || term.Contains("?=") || term.Contains("#(") || term.Contains(" = ")
  || CodeQuote().IsMatch(term) || term.Contains("( )") || AbapVariable().IsMatch(term);

 private static bool IsCodeLine(string line) {
  var t = line.Trim();
  // Boş, çok kısa, yorum ("* ..." veya "\" ...") ve çok uzun satırlar quiz için kullanılmaz.
  return t.Length >= 4 && t.Length <= 120 && !line.StartsWith('*') && !t.StartsWith('"');
 }

 // Açıklamada kavramın kendisi geçiyorsa gizler (cevap açıklamadan okunmasın).
 public static string Mask(string text, string term) {
  var parts = new List<string> { Core(term) };
  var paren = Regex.Match(term, @"\(([^)]+)\)");
  if (paren.Success) parts.Add(paren.Groups[1].Value.Trim());
  foreach (var p in parts.Where(p => p.Length >= 3))
   text = Regex.Replace(text, Regex.Escape(p), "▁▁▁", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
  return text;
 }
 // "REUSE_ALV_LIST_DISPLAY (Klasik Liste)" -> "REUSE_ALV_LIST_DISPLAY"
 public static string Core(string term) { var i = term.IndexOf(" ("); return (i > 0 ? term[..i] : term).Trim(); }

 // Uzun metni cümle veya kelime sınırında kısaltır.
 public static string Shorten(string text, int max) {
  text = text.Trim();
  if (text.Length <= max) return text;
  var cut = text[..max];
  var sentence = cut.LastIndexOfAny(['.', '!', ';']);
  if (sentence >= max / 2) return cut[..(sentence + 1)];
  var space = cut.LastIndexOf(' ');
  return (space > max / 2 ? cut[..space] : cut).TrimEnd(',', ' ') + "…";
 }
}
