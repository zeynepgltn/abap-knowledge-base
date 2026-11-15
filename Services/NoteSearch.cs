using ABAPKnowledgeBase.Data;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using ABAPKnowledgeBase.Models;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Arama sonuçlarını sıralar ve her sonuç için aranan ifadenin geçtiği kısa pasajı çıkarır.
/// Adayları veritabanı bulur (her kelime bir alanda geçmeli); puan ve pasaj burada, bellekte hesaplanır.
/// Puan: tam ifade başlıkta &gt; etikette &gt; kodda &gt; metinde &gt; resimdeki yazıda; çok kelimeli aramada
/// ifade bir arada geçmese de her kelimenin bulunduğu notlar daha düşük puanla listelenir.
/// </summary>
public static partial class NoteSearch {
 public sealed record Hit(Note Note, int Score, string Where, string Before, string Match, string After);

 // Veritabanı filtresiyle (TextMatch) aynı kural: büyük/küçük harf ve İ/I/ı/i farkı gözetilmez.
 // Fold harf harf çevirir, metnin uzunluğu değişmez; bulunan konum orijinal metinde de geçerlidir.
 private static int Find(string? text, string term, int start = 0) =>
  string.IsNullOrEmpty(text) || start >= text.Length ? -1 : TextMatch.Fold(text).IndexOf(TextMatch.Fold(term), start, StringComparison.Ordinal);
 private static int Count(string? text, string term) {
  var count = 0;
  for (var i = Find(text, term); i >= 0 && count < 10; i = Find(text, term, i + term.Length)) count++;
  return count;
 }

 // "VALUE #(" gibi sembollü aramalar bölünmesin diye sadece boşluktan ayrılır.
 public static string[] Words(string query) =>
  query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

 public static List<Hit> Rank(IEnumerable<Note> notes, string query, IReadOnlyDictionary<int, string> ocrByNote) {
  var phrase = query.Trim();
  var words = Words(phrase);
  var hits = new List<Hit>();
  foreach (var note in notes) {
   var ocr = ocrByNote.GetValueOrDefault(note.Id);
   var tags = string.Join(", ", note.TagNames);
   var score = 0;
   if (TextMatch.Fold(note.Title.Trim()) == TextMatch.Fold(phrase)) score += 100;
   else if (Find(note.Title, phrase) is var t && t >= 0) score += t == 0 ? 70 : 60;
   if (Find(tags, phrase) >= 0) score += 25;
   if (Find(note.Code, phrase) >= 0) score += 20;
   if (Count(note.Body, phrase) is var n && n > 0) score += 15 + Math.Min(n, 5) * 3;
   if (Find(ocr, phrase) >= 0) score += 8;
   // Kelimeler ayrı ayrı: her biri notun bir yerinde geçmeli, başlıkta geçen kelime daha değerli.
   if (score == 0 && words.Length > 1) {
    foreach (var w in words) {
     var inTitle = Find(note.Title, w) >= 0;
     var anywhere = inTitle || Find(note.Body, w) >= 0 || Find(note.Code, w) >= 0 || Find(tags, w) >= 0 || Find(ocr, w) >= 0;
     if (!anywhere) { score = 0; break; }
     score += inTitle ? 12 : 4;
    }
   }
   if (score == 0) continue;
   var (where, before, match, after) = Snippet(note, tags, ocr, phrase, words);
   hits.Add(new Hit(note, score, where, before, match, after));
  }
  return hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Note.UpdatedAt).ToList();
 }

 // Pasaj önce metinde, sonra kodda, etiketlerde ve resimdeki yazıda aranır; ifade yoksa ilk kelime aranır.
 private static (string Where, string Before, string Match, string After) Snippet(Note note, string tags, string? ocr, string phrase, string[] words) {
  var sources = new (string Where, string? Text)[] { ("Metin", Clean(note.Body)), ("Kod", Clean(note.Code)), ("Etiket", tags), ("Resimdeki yazı", Clean(ocr)) };
  var terms = words.Length > 1 ? words.Prepend(phrase).ToArray() : new[] { phrase };
  foreach (var term in terms)
   foreach (var (where, text) in sources)
    if (Find(text, term) is var i && i >= 0) {
     var start = Math.Max(0, i - 60);
     var end = Math.Min(text!.Length, i + term.Length + 90);
     return (where, (start > 0 ? "…" : "") + text[start..i], text.Substring(i, term.Length), text[(i + term.Length)..end] + (end < text.Length ? "…" : ""));
    }
  // Sadece başlıkta geçiyorsa notun başı gösterilir.
  var body = Clean(note.Body) ?? "";
  return ("Başlık", "", "", body.Length > 150 ? body[..150] + "…" : body);
 }

 [GeneratedRegex(@"^\s*(```.*|\[resim:\d+\])\s*$", RegexOptions.Multiline)]
 private static partial Regex Markers();
 [GeneratedRegex(@"\s+")]
 private static partial Regex Spaces();
 // Pasajda kod bloğu ve resim işaretleri gösterilmez, satır sonları tek boşluğa iner.
 private static string? Clean(string? text) => string.IsNullOrEmpty(text) ? text : Spaces().Replace(Markers().Replace(text, " "), " ").Trim();

 // Başlıkta vurgulamak için: (öncesi, eşleşen, sonrası). İfade yoksa ilk geçen kelime; hiçbiri yoksa eşleşen boştur.
 public static (string Before, string Match, string After) Split(string text, string query) {
  foreach (var term in Words(query).Prepend(query.Trim()))
   if (term.Length > 0 && Find(text, term) is var i && i >= 0)
    return (text[..i], text.Substring(i, term.Length), text[(i + term.Length)..]);
  return (text, "", "");
 }

 // Vurgulu metni araya boşluk girmeden üretir; her parça HTML olarak kodlanır.
 public static IHtmlContent Highlight(string before, string match, string after) =>
  new HtmlString(WebUtility.HtmlEncode(before) + (match.Length > 0 ? $"<mark>{WebUtility.HtmlEncode(match)}</mark>" : "") + WebUtility.HtmlEncode(after));

 // Sonuçlardaki etiketler (OneNote'tan gelenlerde bölüm adı); her notta kendiliğinden olan "onenote" sayılmaz.
 public static IEnumerable<string> TagsOf(Note note) =>
  note.TagNames.Where(t => !t.Equals("onenote", StringComparison.OrdinalIgnoreCase));
}
