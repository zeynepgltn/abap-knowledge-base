using System.Text.RegularExpressions;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Not gövdesindeki basit işaretleri parçalara ayırır:
///  ``` ile açılıp kapanan satırlar kod bloğudur,
///  tek başına "[resim:3]" satırı notun 3. resmidir, geri kalanı düz metindir.
/// Görünüm her parçayı ayrı çizer; metin her zaman HTML olarak kodlanır.
/// </summary>
public static partial class NoteBody {
 public enum Kind { Text, Code, Image }
 public sealed record Segment(Kind Kind, string Text, int ImageOrder = 0);

 [GeneratedRegex(@"^\s*\[resim:(\d+)\]\s*$")]
 private static partial Regex ImageLine();

 public static IReadOnlyList<Segment> Parse(string? body) {
  var result = new List<Segment>();
  var buffer = new List<string>();
  var inCode = false;
  void Flush(Kind kind) {
   var text = string.Join('\n', buffer);
   buffer.Clear();
   // Kod bloğunun içindeki boşluklar korunur; metinde baştaki/sondaki boş satırlar atılır.
   if (kind == Kind.Text) text = text.Trim('\n', '\r');
   if (text.Trim().Length > 0) result.Add(new Segment(kind, text));
  }
  foreach (var raw in (body ?? "").Replace("\r\n", "\n").Split('\n')) {
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
   buffer.Add(raw);
  }
  // Kapanmamış kod bloğu da kod olarak gösterilir.
  Flush(inCode ? Kind.Code : Kind.Text);
  return result;
 }
}
