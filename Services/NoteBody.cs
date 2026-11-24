using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;
using System.Text.RegularExpressions;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Not gövdesindeki basit işaretleri parçalara ayırır:
///  ``` ile açılıp kapanan satırlar kod bloğudur,
///  tek başına "[resim:3]" satırı notun 3. resmidir, geri kalanı düz metindir.
///  Metindeki ⟦...⟧ fosforlu kalem vurgusudur (okuma ekranında seçip "Vurgula" ile eklenir).
/// Görünüm her parçayı ayrı çizer; metin her zaman HTML olarak kodlanır.
/// </summary>
public static partial class NoteBody {
 public enum Kind { Text, Code, Image }
 // Start/Length: parçanın metninin, satır sonları "\n"e çevrilmiş gövdedeki yeri (vurgu eklerken kullanılır).
 public sealed record Segment(Kind Kind, string Text, int ImageOrder = 0, int Start = 0, int Length = 0);
 // Vurgu işaretleri. "==" seçilmedi: notlarda "=====" satırları ve JavaScript karşılaştırmaları var.
 public const char MarkOpen = '⟦', MarkClose = '⟧';

 [GeneratedRegex(@"^\s*\[resim:(\d+)\]\s*$")]
 private static partial Regex ImageLine();

 public static string Normalize(string? body) => (body ?? "").Replace("\r\n", "\n");

 public static IReadOnlyList<Segment> Parse(string? body) {
  var result = new List<Segment>();
  var buffer = new List<string>();
  var inCode = false;
  var pos = 0;          // satırın gövdedeki başlangıcı
  var bufferStart = 0;  // buffer'daki ilk satırın başlangıcı
  void Flush(Kind kind) {
   var text = string.Join('\n', buffer);
   buffer.Clear();
   var start = bufferStart;
   // Kod bloğunun içindeki boşluklar korunur; metinde baştaki/sondaki boş satırlar atılır.
   if (kind == Kind.Text) {
    var lead = text.Length - text.TrimStart('\n').Length;
    text = text.Trim('\n');
    start += lead;
   }
   if (text.Trim().Length > 0) result.Add(new Segment(kind, text, 0, start, text.Length));
  }
  foreach (var raw in Normalize(body).Split('\n')) {
   var lineStart = pos; pos += raw.Length + 1;
   if (raw.TrimStart().StartsWith("```")) {
    Flush(inCode ? Kind.Code : Kind.Text);
    inCode = !inCode;
    continue;
   }
   var image = inCode ? null : ImageLine().Match(raw);
   if (image is { Success: true }) {
    Flush(Kind.Text);
    result.Add(new Segment(Kind.Image, "", int.Parse(image.Groups[1].Value)));
    continue;
   }
   if (buffer.Count == 0) bufferStart = lineStart;
   buffer.Add(raw);
  }
  // Kapanmamış kod bloğu da kod olarak gösterilir.
  Flush(inCode ? Kind.Code : Kind.Text);
  return result;
 }

 /// <summary>Metni vurgulu/vurgusuz parçalara böler; işaretler atılır. Kapanmamış ⟦ düz metin sayılır.</summary>
 public static List<(string Text, bool Marked)> Runs(string text) {
  var runs = new List<(string, bool)>();
  var i = 0;
  while (i < text.Length) {
   var open = text.IndexOf(MarkOpen, i);
   var close = open < 0 ? -1 : text.IndexOf(MarkClose, open + 1);
   if (open < 0 || close < 0) { runs.Add((text[i..], false)); break; }
   if (open > i) runs.Add((text[i..open], false));
   if (close > open + 1) runs.Add((text[(open + 1)..close], true));
   i = close + 1;
  }
  return runs;
 }

 /// <summary>İşaretsiz metin (quiz, arama pasajı vb. için).</summary>
 public static string Plain(string? text) =>
  string.IsNullOrEmpty(text) ? text ?? "" : text.Replace(MarkOpen.ToString(), "").Replace(MarkClose.ToString(), "");

 /// <summary>
 /// Bir metin parçasında (gövdedeki metin ya da kod bölümü) [start, end) aralığını vurgular ya da vurguyu kaldırır.
 /// Konumlar işaretsiz metindedir. Geçersiz istek için null döner; aksi halde yeni gövde.
 /// </summary>
 public static string? SetMark(string? body, int segmentIndex, int start, int end, bool on) {
  var normalized = Normalize(body);
  var segments = Parse(normalized);
  if (segmentIndex < 0 || segmentIndex >= segments.Count || segments[segmentIndex].Kind == Kind.Image) return null;
  var seg = segments[segmentIndex];
  return SetMarkText(seg.Text, start, end, on) is { } text ? normalized[..seg.Start] + text + normalized[(seg.Start + seg.Length)..] : null;
 }

 /// <summary>Tek bir metnin tamamında (ör. notun ayrı "ABAP kodu" alanı) vurgu ekler/kaldırır. Çakışan ve bitişik vurgular birleşir.</summary>
 public static string? SetMarkText(string? source, int start, int end, bool on) {
  var runs = Runs(Normalize(source));
  var plain = string.Concat(runs.Select(r => r.Text));
  if (start < 0 || end > plain.Length || start >= end) return null;
  var mask = new bool[plain.Length];
  var k = 0;
  foreach (var (text, marked) in runs) { for (var j = 0; j < text.Length; j++) mask[k++] = marked; }
  // Seçimin başındaki/sonundaki boşluklar vurgulanmaz.
  if (on) { while (start < end && char.IsWhiteSpace(plain[start])) start++; while (end > start && char.IsWhiteSpace(plain[end - 1])) end--; }
  if (start >= end) return null;
  for (var j = start; j < end; j++) mask[j] = on;
  var sb = new StringBuilder();
  for (var j = 0; j < plain.Length; j++) {
   if (mask[j] && (j == 0 || !mask[j - 1])) sb.Append(MarkOpen);
   sb.Append(plain[j]);
   if (mask[j] && (j == plain.Length - 1 || !mask[j + 1])) sb.Append(MarkClose);
  }
  return sb.ToString();
 }

 /// <summary>Metni vurgulu çizer: her parça data-o (işaretsiz metindeki konum) taşır; site.js seçimi buradan hesaplar.</summary>
 public static IHtmlContent RenderRuns(string? text) {
  var sb = new StringBuilder(); var offset = 0;
  foreach (var (run, marked) in Runs(Normalize(text))) {
   var encoded = WebUtility.HtmlEncode(run);
   sb.Append(marked ? $"<mark class=\"hl\" data-o=\"{offset}\" title=\"Vurguyu kaldırmak için tıkla\">{encoded}</mark>" : $"<span data-o=\"{offset}\">{encoded}</span>");
   offset += run.Length;
  }
  return new HtmlString(sb.ToString());
 }
}
