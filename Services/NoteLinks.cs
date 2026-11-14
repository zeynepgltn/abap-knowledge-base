using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Formdaki etiket ve kaynak metnini Tags/NoteTags/Sources tablolarına bağlar.
/// Var olan etiket/kaynak yeniden kullanılır, yoksa oluşturulur. Toplu içe aktarmada aynı örnek
/// kullanılırsa aynı etiket iki kez eklenmez (henüz kaydedilmemiş olanlar da bellekte tutulur).
/// </summary>
public sealed class NoteLinks {
 private readonly NotesDbContext db;
 private readonly Dictionary<string, Tag> tags;
 private readonly Dictionary<string, Source> sources;

 private NoteLinks(NotesDbContext db, Dictionary<string, Tag> tags, Dictionary<string, Source> sources) {
  this.db = db; this.tags = tags; this.sources = sources;
 }

 public static async Task<NoteLinks> LoadAsync(NotesDbContext db) => new(db,
  (await db.Tags.ToListAsync()).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase),
  (await db.Sources.ToListAsync()).ToDictionary(Key, StringComparer.OrdinalIgnoreCase));

 private static string Key(Source s) => $"{s.Kind}|{s.Name}|{s.Section}";

 // "a, b ,a" -> ["a", "b"]; boş ve tekrar eden adlar atılır, 100 karakterle sınırlanır.
 public static List<string> ParseTags(string? text) =>
  (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
   .Select(t => t.Length > 100 ? t[..100] : t).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

 // "OneNote: defter › bölüm" OneNote kaynağıdır; geri kalan her metin dosya adı sayılır.
 public static Source? ParseSource(string? text) {
  text = text?.Trim();
  if (string.IsNullOrEmpty(text)) return null;
  const string prefix = "OneNote: ";
  var sep = text.IndexOf(" › ", StringComparison.Ordinal);
  if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && sep > prefix.Length)
   return new Source { Kind = Source.OneNote, Name = Cut(text[prefix.Length..sep], 200), Section = Cut(text[(sep + 3)..], 300) };
  return new Source { Kind = Source.File, Name = Cut(text, 200) };
 }
 private static string Cut(string s, int max) => (s.Length > max ? s[..max] : s).Trim();

 public Source? SourceFor(string? text) {
  var parsed = ParseSource(text);
  if (parsed is null) return null;
  if (sources.TryGetValue(Key(parsed), out var existing)) return existing;
  db.Sources.Add(parsed);
  return sources[Key(parsed)] = parsed;
 }

 // Notun etiketlerini metindekilerle değiştirir; kaldırılan bağlantılar silinir, yeni etiketler eklenir.
 public void SetTags(Note note, string? text) {
  var wanted = ParseTags(text);
  foreach (var link in note.NoteTags.Where(l => l.Tag is null || !wanted.Contains(l.Tag.Name, StringComparer.OrdinalIgnoreCase)).ToList())
   note.NoteTags.Remove(link);
  foreach (var name in wanted.Where(n => !note.NoteTags.Any(l => string.Equals(l.Tag?.Name, n, StringComparison.OrdinalIgnoreCase)))) {
   if (!tags.TryGetValue(name, out var tag)) { tag = new Tag { Name = name }; db.Tags.Add(tag); tags[name] = tag; }
   note.NoteTags.Add(new NoteTag { Note = note, Tag = tag });
  }
 }

 public void Apply(Note note, string? tagsText, string? sourceText) {
  SetTags(note, tagsText);
  note.Source = SourceFor(sourceText);
  if (note.Source is null) note.SourceId = null;
 }

 // Düzenleme formu için notun etiket/kaynak metnini doldurur (NoteTags.Tag ve Source yüklü olmalı).
 public static void FillText(Note note) {
  note.TagsText = string.Join(", ", note.TagNames);
  note.SourceText = note.Source?.ToString();
 }

 // Hiçbir notta kullanılmayan etiket ve kaynakları temizler.
 public static async Task RemoveOrphansAsync(NotesDbContext db) {
  await db.Tags.Where(t => !t.NoteTags.Any()).ExecuteDeleteAsync();
  await db.Sources.Where(s => !s.Notes.Any()).ExecuteDeleteAsync();
 }
}
