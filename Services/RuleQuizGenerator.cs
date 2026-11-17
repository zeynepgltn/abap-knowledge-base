using System.Text.RegularExpressions;
using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Kural tabanlı soru üretici (quiz aşama 1). Makine öğrenmesi kullanmaz; notlardaki
/// "kavram: açıklama" çiftlerinden ve kod satırlarından soru kurar.
///
/// Günün quiz'i: 3 mantık + 2 kod, 1 kolay + 3 orta + 1 zor.
///   1. Mantık · Kolay  "kavram-bul"   açıklaması verilen kavram hangisi?
///   2. Mantık · Orta   "tanim-sec"    kavramın doğru açıklaması hangisi?  (ya da "bosluk-doldur", aşağıda)
///   3. Mantık · Orta   "farkli-olan"  hangisi bu notun kavramlarından biri değil?
///   4. Kod · Orta      "kod-ne-yapar" bu kod satırı ne yapar?  (yoksa "kod-tamamla": boşluğa ne gelir?)
///   5. Kod · Zor       "hatali-satir" kod parçasındaki hatalı satır hangisi?
///
/// Aşama 2: "bosluk-doldur" notun bir cümlesinde TF-IDF ile bulunmuş anahtar terimi gizler
/// (terimleri ml/anahtar_terimler.py hesaplar). Böylece "kavram: açıklama" düzeninde yazılmamış notlar da sorulabilir.
///
/// Not seçimi (aralıklı tekrar): yanlış bilinip tekrarı gelmiş notlar önce, sonra hiç sorulmamışlar,
/// sonra tekrarı gelmiş diğerleri (düşük kutu önce). Her soru farklı nottan, mümkünse farklı kategoriden.
/// </summary>
public sealed partial class RuleQuizGenerator(Random rng) {
 // v2: TF-IDF anahtar terimleriyle boşluk doldurma eklendi (quiz aşama 2).
 public const string Name = "kural-v2";

 private sealed record Slot(string Kind, string Difficulty, Func<NoteMaterial, QuizQuestion?>[] Makers);

 private IReadOnlyList<NoteMaterial> pool = [];

 public List<QuizQuestion> Generate(IReadOnlyList<NoteMaterial> materials, IReadOnlyDictionary<int, NoteReview> reviews, DateOnly today) {
  pool = materials;
  var ranked = materials
   .Select(m => (m, priority: Priority(reviews.GetValueOrDefault(m.Note.Id), today), tie: rng.Next()))
   .OrderBy(x => x.priority).ThenBy(x => x.tie).Select(x => x.m).ToList();

  // 2. soru günden güne boşluk doldurma ya da tanım seçme olur; biri çıkmazsa diğeri denenir.
  Func<NoteMaterial, QuizQuestion?>[] second = rng.Next(2) == 0 ? [FillBlank, ChooseDefinition] : [ChooseDefinition, FillBlank];
  var slots = new Slot[] {
   new(QuizKind.Logic, QuizDifficulty.Easy, [FindConcept]),
   new(QuizKind.Logic, QuizDifficulty.Medium, second),
   new(QuizKind.Logic, QuizDifficulty.Medium, [OddOneOut, ChooseDefinition, FillBlank]),
   new(QuizKind.Code, QuizDifficulty.Medium, [WhatDoesCodeDo, CompleteCode]),
   new(QuizKind.Code, QuizDifficulty.Hard, [FindWrongLine]),
  };
  var result = new QuizQuestion?[slots.Length];
  var usedNotes = new HashSet<int>();
  var usedCategories = new HashSet<string>();
  // Kod malzemesi daha az notta var: önce kod soruları doldurulur, sonra mantık soruları.
  foreach (var i in new[] { 4, 3, 0, 1, 2 }) {
   var slot = slots[i];
   // Önce yeni bir kategoriden not aranır, bulunamazsa kategori tekrarına izin verilir.
   foreach (var newCategoryOnly in new[] { true, false }) {
    foreach (var m in ranked) {
     if (usedNotes.Contains(m.Note.Id) || (newCategoryOnly && usedCategories.Contains(m.Category))) continue;
     var q = slot.Makers.Select(make => make(m)).FirstOrDefault(q => q is not null);
     if (q is null) continue;
     q.NoteId = m.Note.Id; q.Kind = slot.Kind; q.Difficulty = slot.Difficulty;
     result[i] = q;
     usedNotes.Add(m.Note.Id); usedCategories.Add(m.Category);
     break;
    }
    if (result[i] is not null) break;
   }
  }
  var questions = result.Where(q => q is not null).Select(q => q!).ToList();
  for (var i = 0; i < questions.Count; i++) questions[i].Order = i + 1;
  return questions;
 }

 // Küçük sayı önce sorulur.
 private static int Priority(NoteReview? r, DateOnly today) {
  if (r is null) return 1;                                       // hiç sorulmamış
  if (r.DueDate > today) return 10 + (r.DueDate.DayNumber - today.DayNumber); // tekrarı gelmemiş
  if (r.Box == 1 && r.WrongCount > 0) return 0;                  // yanlış bilinmiş, tekrarı gelmiş
  return 1 + r.Box;                                              // tekrarı gelmiş
 }

 // ---------- Mantık soruları ----------

 // Kolay: açıklama verilir, kavram seçilir.
 private QuizQuestion? FindConcept(NoteMaterial m) {
  var candidates = m.Concepts.Where(p => p.Definition.Length >= 25).ToList();
  foreach (var p in Shuffle(candidates)) {
   var clue = QuizMaterial.Shorten(QuizMaterial.Mask(p.Definition, p.Term), 260);
   if (clue.Replace("▁", "").Length < 20) continue;
   var wrong = PickOthers(m, p, x => !x.IsCode && x.Term.Length <= 60, x => x.Term,
    x => !Same(x.Term, p.Term) && !Contains(x.Term, p.Term) && !Contains(p.Term, x.Term));
   if (wrong.Count < 3) continue;
   return Build("kavram-bul", $"Aşağıdaki açıklama hangi kavrama ait?\n\n“{clue}”", null, p.Term, wrong,
    $"Doğru cevap “{p.Term}”. Notta: “{QuizMaterial.Shorten(p.Definition, 240)}”");
  }
  return null;
 }

 // Orta: kavram verilir, doğru açıklama seçilir. Yanlış şıklar önce aynı nottaki diğer açıklamalardan
 // gelir; aynı konudan oldukları için ayırt etmek düşünmeyi gerektirir.
 private QuizQuestion? ChooseDefinition(NoteMaterial m) {
  foreach (var p in Shuffle(m.Concepts.Where(p => p.Definition.Length >= 20).ToList())) {
   var right = Def(p);
   var wrong = PickOthers(m, p, x => !x.IsCode && x.Definition.Length >= 15, Def,
    x => !Same(x.Term, p.Term) && !Same(Def(x), right));
   if (wrong.Count < 3) continue;
   return Build("tanim-sec", $"“{p.Term}” için hangi açıklama doğru?", null, right, wrong,
    $"“{p.Term}”: {QuizMaterial.Shorten(p.Definition, 240)}");
  }
  return null;
 }
 private static string Def(QuizPair p) => QuizMaterial.Shorten(QuizMaterial.Mask(p.Definition, p.Term), 170);

 // Orta: notun üç kavramı + başka kategoriden bir kavram; farklı olan bulunur.
 private QuizQuestion? OddOneOut(NoteMaterial m) {
  var own = m.Concepts.Where(p => p.Term.Length <= 40).ToList();
  // Dört şık aynı biçimde olmalı (hepsi kod/Tcode ya da hepsi terim): biçimi farklı olan şık
  // içeriği bilinmeden "farklı olan" diye seçilebilir.
  foreach (var shape in Shuffle(own.Select(p => Shape(p.Term)).Distinct().ToList())) {
   var same = own.Where(p => Shape(p.Term) == shape).ToList();
   if (same.Count < 3) continue;
   var three = Shuffle(same).Take(3).ToList();
   var noteText = TextMatch.Fold(m.AllText);
   var outsiders = pool.Where(o => o.Category != m.Category)
    .SelectMany(o => o.Concepts.Where(p => p.Term.Length <= 40 && Shape(p.Term) == shape).Select(p => (o, p)))
    .Where(x => !three.Any(t => Same(t.Term, x.p.Term)) && !Related(noteText, x.p.Term))
    .ToList();
   if (outsiders.Count == 0) continue;
   var (other, odd) = outsiders[rng.Next(outsiders.Count)];
   return Build("farkli-olan", $"Aşağıdakilerden hangisi “{m.Note.Title}” notunda anlatılan kavramlardan biri DEĞİL?",
    null, odd.Term, three.Select(t => t.Term).ToList(),
    $"“{odd.Term}”, {other.Category} kategorisindeki “{other.Note.Title}” notundan. " +
    $"Diğerleri bu notta geçiyor: {string.Join(", ", three.Select(t => t.Term))}.");
  }
  return null;
 }

 // Kavramın biçimi: "kod" = SE10, MONTH_TAB, cl_gui_alv_grid gibi tek parça teknik ad; "terim" = kelimelerden oluşan ad.
 private static string Shape(string term) =>
  CodeLikeTerm().IsMatch(QuizMaterial.Core(term)) ? "kod" : "terim";
 // Tamamı büyük harf (APPEND, SE10) ya da rakam/alt çizgi içeren tek parça ad (lv_x, cl_gui_alv_grid). "Prefix" terimdir.
 // "CORRESPONDING FIELDS OF" gibi büyük harfli ABAP ifadeleri de kod sayılır.
 [GeneratedRegex(@"^(?:[A-Z0-9_/~\-]{2,}|[A-Z][A-Z0-9_ #()]*[A-Z0-9)]|[A-Za-z0-9/~\-]*[0-9_][A-Za-z0-9_/~\-]*)$")]
 private static partial Regex CodeLikeTerm();

 // Dışarıdan gelen kavram notla ilgili mi? Tamamı ya da anlamlı kelimelerinin yarısı notta geçiyorsa
 // ilgilidir ("Debug ve Takip Zorluğu" ↔ nottaki "takip zorluğu") ve "farklı olan" şıkkı olamaz.
 private static bool Related(string foldedNoteText, string term) {
  var core = TextMatch.Fold(QuizMaterial.Core(term));
  if (core.Length >= 3 && foldedNoteText.Contains(core, StringComparison.Ordinal)) return true;
  var words = WordSplit().Split(core).Where(w => w.Length >= 4 && !FillerWords.Contains(w)).Distinct().ToList();
  if (words.Count == 0) return false;
  var found = words.Count(w => foldedNoteText.Contains(w, StringComparison.Ordinal));
  return found * 2 >= words.Count;
 }
 [GeneratedRegex(@"[^\p{L}\p{N}_]+")]
 private static partial Regex WordSplit();
 private static readonly HashSet<string> FillerWords = ["veya", "gibi", "için", "ile", "olan", "olarak", "daha", "kadar"];

 // Orta (aşama 2): notun TF-IDF anahtar terimlerinden biri bir cümlede gizlenir, boşluğa hangisinin geleceği seçilir.
 // Yanlış şıklar başka notların aynı biçimdeki anahtar terimleridir (önce aynı kategoriden).
 private QuizQuestion? FillBlank(NoteMaterial m) {
  var noteText = TextMatch.Fold(m.AllText);
  foreach (var term in m.KeyTerms.Take(6).Where(t => BlankWorthy(t, m.AllText))) {
   var rx = TermRegex(term);
   var sentences = m.Sentences.Where(s => s.Length is >= 40 and <= 240 && rx.IsMatch(s) && !LooksLikeCode(s)).ToList();
   if (sentences.Count == 0) continue;
   var wrong = PickTerms(m, term, noteText);
   if (wrong.Count < 3) continue;
   foreach (var s in Shuffle(sentences)) {
    var masked = rx.Replace(s, "▁▁▁▁");
    // Boşluk dışında en az 5 kelime kalmalı; yoksa cümle ipucu vermez.
    if (WordSplit().Split(masked.Replace("▁", " ")).Count(w => w.Length >= 2) < 5) continue;
    var title = rx.Replace(m.Note.Title, "▁▁▁▁");
    return Build("bosluk-doldur", $"“{title}” notundan bir cümle. Boşluğa hangisi gelmeli?\n\n“{masked}”", null, term, wrong,
     $"Doğru cevap “{term}”; bu notu en iyi ayırt eden terimlerden biri. Notta: “{s}”");
   }
  }
  return null;
 }

 // Boşluk yapılmaya değer terim mi? Teknik adlar (ALV, SELECTION-SCREEN, cl_salv_table) ya da cümle ortasında
 // da büyük harfle yazılan özel adlar (Smartform, Field Catalog). "pencerenin" gibi çekimli sıradan kelimeler
 // ve nota özgü değişken/Z nesnesi adları (lv_x, gs_sort-spos, ZGZ_P_...) elenir.
 private static bool BlankWorthy(string term, string allText) {
  if (term.Length is < 3 or > 40) return false;
  if (LocalName().IsMatch(term)) return false;
  if (Shape(term) == "kod") return true;
  return char.IsUpper(term[0]) && Regex.IsMatch(allText, @"[\p{Ll}\d,]\s+" + Regex.Escape(term).Replace("\\ ", "\\s+") + @"(?![\w-])");
 }
 // ABAP değişken önekleri (lv_, GS_, it_, p_, s_, wa_, fs_...) ve müşteri nesneleri (Z*/Y*): büyük/küçük harf fark etmez.
 [GeneratedRegex(@"^(?:[lgimecr][vstorfx]_|wa_|fs_|[ps]_|[ZY][A-Z0-9_]{3,}$)", RegexOptions.IgnoreCase)]
 private static partial Regex LocalName();

 private static Regex TermRegex(string term) =>
  new($@"(?<![\w-]){Regex.Escape(term).Replace("\\ ", "\\s+")}(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

 private static bool LooksLikeCode(string s) =>
  s.Contains("->") || s.Contains("=>") || s.Contains(" = ") || s.Contains("#(") || s.Contains("( )");

 // Hepsi büyük harf (ALV, BOUND) ya da karışık (Smartform, cl_salv_table): şıklar aynı yazımda olmalı.
 private static bool AllCaps(string t) => t == t.ToUpperInvariant();

 // Yazım kalıbı: büyük harf → A, küçük → a, rakam → 9, tekrarlar tek sayılır. SE37 → "A9", SE16/SE16N → "A9/A9A",
 // MODULE → "A", Field Catalog → "Aa Aa". Şıklar önce cevapla aynı kalıptan seçilir (işlem koduna işlem kodu).
 private static string Pattern(string t) {
  var sb = new System.Text.StringBuilder();
  foreach (var c in t) {
   var k = char.IsUpper(c) ? 'A' : char.IsLower(c) ? 'a' : char.IsDigit(c) ? '9' : c;
   if (sb.Length == 0 || sb[^1] != k) sb.Append(k);
  }
  return sb.ToString();
 }

 // Yanlış şıklar: başka notların anahtar terimleri. Sıra: aynı kalıp + aynı kategori, aynı kalıp + diğer kategoriler,
 // sonra sadece aynı biçim (kod/terim, büyük harf/karışık). Sıradan Türkçe kelimeler ("Müdür") şık olmasın diye
 // kelime biçimindeki şıklar teknik terimlerdeki gibi sadece İngilizce harflerden oluşmalı.
 private List<string> PickTerms(NoteMaterial m, string answer, string foldedNoteText) {
  var shape = Shape(answer); var caps = AllCaps(answer); var pattern = Pattern(answer);
  var sameCategory = pool.Where(o => o != m && o.Category == m.Category).ToList();
  var otherCategory = pool.Where(o => o != m && o.Category != m.Category).ToList();
  var picked = new List<string>();
  foreach (var (notes, strict) in new[] { (sameCategory, true), (otherCategory, true), (sameCategory, false), (otherCategory, false) }) {
   var candidates = notes.SelectMany(o => o.KeyTerms.Take(6).Where(t => BlankWorthy(t, o.AllText))).ToList();
   foreach (var t in Shuffle(candidates)) {
    if (Shape(t) != shape || AllCaps(t) != caps || (strict && Pattern(t) != pattern)) continue;
    if (shape == "terim" && !t.All(c => c < 128)) continue;
    if (Same(t, answer) || Related(foldedNoteText, t) || picked.Any(p => Same(p, t))) continue;
    picked.Add(t);
    if (picked.Count == 3) return picked;
   }
  }
  return picked;
 }

 // ---------- Kod soruları ----------

 // Orta: açıklamalı bir kod satırı gösterilir, ne yaptığı seçilir.
 private QuizQuestion? WhatDoesCodeDo(NoteMaterial m) {
  foreach (var p in Shuffle(m.CodePairs.Where(p => p.Definition.Length >= 15).ToList())) {
   var right = QuizMaterial.Shorten(p.Definition, 170);
   var wrong = PickOthers(m, p, x => x.IsCode && x.Definition.Length >= 15, x => QuizMaterial.Shorten(x.Definition, 170),
    x => !Same(x.Term, p.Term) && !Same(QuizMaterial.Shorten(x.Definition, 170), right));
   if (wrong.Count < 3) continue;
   return Build("kod-ne-yapar", "Bu kod ne yapar?", p.Term, right, wrong,
    $"Notta: “{QuizMaterial.Shorten(p.Definition, 240)}”");
  }
  return null;
 }

 // Kod tamamlama için anahtar kelime aileleri: boşluğa ailenin bir üyesi gelir, şıklar aynı aileden.
 private static readonly string[][] Families = [
  ["->", "=>", "~", "-"],
  ["EXPORTING", "IMPORTING", "CHANGING", "TABLES"],
  ["APPEND", "INSERT", "MODIFY", "COLLECT"],
  ["LOOP AT", "READ TABLE", "DELETE", "SORT"],
  ["TYPE REF TO", "TYPE TABLE OF", "LIKE", "TYPE RANGE OF"],
  ["VALUE #(", "NEW #(", "CONV #(", "CORRESPONDING #("],
  ["CALL FUNCTION", "CALL METHOD", "PERFORM", "SUBMIT"],
  ["ENDIF", "ENDLOOP", "ENDDO", "ENDMETHOD", "ENDCASE", "ENDTRY", "ENDSELECT", "ENDWHILE"],
 ];
 // Boşluk yapılabilecek kelimeler (ailenin her üyesi değil: "-" ve "~" sadece yanlış şık olarak kullanılır).
 private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase) {
  "->", "=>", "EXPORTING", "IMPORTING", "CHANGING", "TABLES", "APPEND", "INSERT", "MODIFY", "COLLECT", "LOOP AT", "READ TABLE",
  "TYPE REF TO", "TYPE TABLE OF", "VALUE #(", "NEW #(", "CONV #(", "CORRESPONDING #(", "CALL FUNCTION", "CALL METHOD",
  "ENDIF", "ENDLOOP", "ENDDO", "ENDMETHOD", "ENDCASE", "ENDTRY", "ENDSELECT", "ENDWHILE" };

 // Orta (yedek): koddan bir anahtar kelime gizlenir, boşluğa hangisinin geleceği seçilir.
 private QuizQuestion? CompleteCode(NoteMaterial m) {
  var sources = m.CodeBlocks.Select(b => (lines: b, block: true))
   .Concat(m.CodePairs.Select(p => (lines: new List<string> { p.Term }, block: false))).ToList();
  var options = new List<(List<string> lines, int line, Match match, string[] family)>();
  foreach (var (lines, _) in sources)
   for (var i = 0; i < lines.Count; i++)
    foreach (var family in Families)
     foreach (var key in family.Where(Targets.Contains)) {
      var match = KeywordRegex(key).Match(lines[i]);
      // Blok kapatan kelime ancak açan kelime de görünüyorsa sorulabilir.
      if (match.Success && (!key.StartsWith("END") || Opens(lines, key))) options.Add((lines, i, match, family));
     }
  if (options.Count == 0) return null;
  var (src, idx, hit, fam) = options[rng.Next(options.Count)];
  var original = src[idx];
  var lower = hit.Value.Any(char.IsLetter) && hit.Value == hit.Value.ToLowerInvariant();
  string Style(string s) => lower ? s.ToLowerInvariant() : s;
  var right = Style(fam.First(f => Same(f, hit.Value)));
  var wrong = Shuffle(fam.Where(f => !Same(f, hit.Value)).ToList()).Take(3).Select(Style).ToList();
  var masked = original[..hit.Index] + "▁▁▁▁" + original[(hit.Index + hit.Length)..];
  var (window, _) = Window(src, idx, 6);
  var code = string.Join("\n", window.Select(i => i == idx ? masked : src[i]));
  return Build("kod-tamamla", "Boşluğa hangisi gelmeli?", code, right, wrong, $"Doğrusu: {original.Trim()}");
 }

 private static Regex KeywordRegex(string key) {
  var body = Regex.Escape(key).Replace("\\ ", "\\s+");
  // Kelimelerde tam kelime eşleşmesi; "->" gibi sembollerde gerekmez.
  return char.IsLetter(key[0])
   ? new Regex($"(?<![\\w-]){body}(?![\\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
   : new Regex(body, RegexOptions.CultureInvariant);
 }

 private static readonly Dictionary<string, string> Openers = new(StringComparer.OrdinalIgnoreCase) {
  ["ENDIF"] = "IF", ["ENDLOOP"] = "LOOP AT", ["ENDDO"] = "DO", ["ENDWHILE"] = "WHILE", ["ENDCASE"] = "CASE",
  ["ENDMETHOD"] = "METHOD", ["ENDTRY"] = "TRY", ["ENDSELECT"] = "SELECT", ["ENDCLASS"] = "CLASS", ["ENDFORM"] = "FORM" };
 private static bool Opens(List<string> lines, string closer) =>
  Openers.TryGetValue(closer, out var opener) && lines.Any(l => KeywordRegex(opener).IsMatch(l) && !KeywordRegex(closer).IsMatch(l));

 // Zor kod sorusundaki hatalar: her kural kesin bir sözdizimi/kullanım hatası üretir.
 private sealed record Mutation(Regex Pattern, Func<Match, string> Replace, Func<Match, string> Why);
 private static readonly Mutation[] Mutations = [
  new(new Regex(@"\b((?:lo|lr|go|gr|mo|ro|io|eo|r)_\w+)->", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + "=>",
   m => $"{m.Groups[1].Value} bir nesne referansı; örnek (instance) üyelerine '->' ile erişilir. '=>' sadece sınıf adıyla statik erişimde kullanılır."),
  new(new Regex(@"\b((?:cl|zcl|ycl|lcl|cx|zcx|if|zif)_\w+)=>", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + "->",
   m => $"{m.Groups[1].Value} bir sınıf/arayüz adı; statik üyelerine '=>' ile erişilir. '->' sadece nesne referansı üzerinden kullanılır."),
  new(new Regex(@"\b(call)(\s+)(function)\b", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + m.Groups[2].Value + Case(m.Groups[3].Value, "METHOD"),
   _ => "Fonksiyon modülü 'CALL FUNCTION' ile çağrılır; 'CALL METHOD' sınıf metotları içindir."),
  new(new Regex(@"\b(append\b.+?\s)(to)\b", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + Case(m.Groups[2].Value, "INTO"),
   _ => "APPEND satırı tablonun sonuna ekler ve 'TO' ile yazılır; 'INTO', INSERT ... INTO içindir."),
  new(new Regex(@"\b(read\s+table\b.+?\s)(into)\b", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + Case(m.Groups[2].Value, "TO"),
   _ => "READ TABLE ile okunan satır 'INTO' (veya ASSIGNING / REFERENCE INTO) ile alınır."),
  new(new Regex(@"\b(loop\s+at\b.+?\s)(into)\b", RegexOptions.IgnoreCase),
   m => m.Groups[1].Value + Case(m.Groups[2].Value, "TO"),
   _ => "LOOP AT ile dönülen satır 'INTO' (veya ASSIGNING / REFERENCE INTO) ile alınır."),
 ];
 private static string Case(string like, string word) => like == like.ToLowerInvariant() ? word.ToLowerInvariant() : word;

 // Zor: kod parçasının bir satırı bozulur, hangi satırın hatalı olduğu bulunur.
 private QuizQuestion? FindWrongLine(NoteMaterial m) {
  var sources = m.CodeBlocks.Where(b => b.Count >= 3).ToList();
  var codeLines = m.CodePairs.Select(p => p.Term).Distinct().ToList();
  if (codeLines.Count >= 3) sources.Add(codeLines);
  foreach (var lines in Shuffle(sources)) {
   foreach (var idx in Shuffle(Enumerable.Range(0, lines.Count).ToList())) {
    // Kapatıcı bozulacaksa açan satır kendinden önce olmalı (yoksa hangisinin yanlış olduğu belirsiz).
    var mutated = (OpenerLine(lines, idx) >= 0 ? MutateBlockEnd(lines, idx) : null) ?? MutateLine(lines[idx]);
    if (mutated is null) continue;
    var (newLine, why) = mutated.Value;
    var (window, start) = Window(lines, idx, 7);
    // Blok kapatıcı bozulduysa, açan satır (IF/LOOP/SELECT...) da görünsün: yoksa soru çözülemez.
    var opener = OpenerLine(lines, idx);
    if (opener >= 0 && opener < start) {
     if (idx - opener >= 7) continue;
     start = opener;
     window = Enumerable.Range(start, idx - start + 1).ToList();
    }
    var shown = window.Select(i => i == idx ? newLine : lines[i]).ToList();
    if (shown.Count < 3) continue;
    var code = string.Join("\n", shown.Select((l, n) => $"{n + 1,2} │ {l}"));
    var wrongLine = idx - start + 1;
    var others = Shuffle(Enumerable.Range(1, shown.Count).Where(n => n != wrongLine).ToList()).Take(3).ToList();
    string Label(int n) => $"{n}. satır: {QuizMaterial.Shorten(shown[n - 1].Trim(), 60)}";
    var choices = others.Append(wrongLine).OrderBy(n => n).ToList();
    var q = Build("hatali-satir", "Bu kod parçasındaki hatalı satır hangisi?", code, Label(wrongLine),
     choices.Where(n => n != wrongLine).Select(Label).ToList(),
     $"{wrongLine}. satır hatalı. {why} Doğrusu: {lines[idx].Trim()}", keepOrder: true);
    // Satır numaraları sırayla gösterilir; doğru şık kendi yerine konur.
    q.Choices = choices.Select(Label).ToList();
    q.CorrectIndex = choices.IndexOf(wrongLine);
    return q;
   }
  }
  return null;
 }

 // Blok kapatan satırı başka bir bloğun kapatıcısıyla değiştirir (LOOP AT ... ENDIF gibi).
 private (string, string)? MutateBlockEnd(List<string> lines, int idx) {
  var match = Regex.Match(lines[idx], @"^(\s*)(END(?:IF|LOOP|DO|WHILE|CASE|METHOD|TRY|SELECT))\b", RegexOptions.IgnoreCase);
  if (!match.Success || !Opens(lines, match.Groups[2].Value)) return null;
  var closer = match.Groups[2].Value;
  var others = new[] { "ENDIF", "ENDLOOP", "ENDDO", "ENDCASE" }.Where(c => !Same(c, closer)).ToList();
  var wrong = Case(closer, others[rng.Next(others.Count)]);
  var opener = Openers[closer];
  return (match.Groups[1].Value + wrong + lines[idx][match.Length..],
   $"{opener} ile açılan blok {closer.ToUpperInvariant()} ile kapanmalı; {wrong.ToUpperInvariant()} başka bir bloğu kapatır.");
 }

 // idx satırı bir blok kapatıcıysa (ENDIF...), onu açan en yakın önceki satırın indeksi; değilse -1.
 private static int OpenerLine(List<string> lines, int idx) {
  var match = Regex.Match(lines[idx], @"^\s*(END(?:IF|LOOP|DO|WHILE|CASE|METHOD|TRY|SELECT))\b", RegexOptions.IgnoreCase);
  if (!match.Success || !Openers.TryGetValue(match.Groups[1].Value, out var opener)) return -1;
  for (var i = idx - 1; i >= 0; i--)
   if (KeywordRegex(opener).IsMatch(lines[i]) && !KeywordRegex(match.Groups[1].Value).IsMatch(lines[i])) return i;
  return -1;
 }

 private static (string, string)? MutateLine(string line) {
  foreach (var mut in Mutations) {
   var match = mut.Pattern.Match(line);
   if (!match.Success) continue;
   return (line[..match.Index] + mut.Replace(match) + line[(match.Index + match.Length)..], mut.Why(match));
  }
  return null;
 }

 // ---------- Yardımcılar ----------

 // Yanlış şıklar: önce aynı nottan, sonra aynı kategoriden, en son tüm notlardan.
 private List<string> PickOthers(NoteMaterial m, QuizPair answer, Func<QuizPair, bool> kind, Func<QuizPair, string> text, Func<QuizPair, bool> ok) {
  var picked = new List<string>();
  var tiers = new[] {
   m.Pairs.AsEnumerable(),
   pool.Where(o => o != m && o.Category == m.Category).SelectMany(o => o.Pairs),
   pool.Where(o => o != m && o.Category != m.Category).SelectMany(o => o.Pairs) };
  foreach (var tier in tiers) {
   foreach (var p in Shuffle(tier.Where(p => p != answer && kind(p) && ok(p)).ToList())) {
    var t = text(p);
    if (picked.Any(x => Same(x, t))) continue;
    picked.Add(t);
    if (picked.Count == 3) return picked;
   }
  }
  return picked;
 }

 private QuizQuestion Build(string type, string prompt, string? code, string right, List<string> wrong, string explanation, bool keepOrder = false) {
  var choices = wrong.Take(3).Append(right).ToList();
  if (!keepOrder) choices = Shuffle(choices);
  return new QuizQuestion { Type = type, Prompt = prompt, Code = code, Choices = choices,
   CorrectIndex = choices.IndexOf(right), Explanation = explanation };
 }

 // Gösterilecek kod penceresi: hedef satırın çevresinden en fazla max satır.
 private static (List<int> window, int start) Window(List<string> lines, int idx, int max) {
  var start = Math.Clamp(idx - max / 2, 0, Math.Max(0, lines.Count - max));
  var end = Math.Min(lines.Count, start + max);
  return (Enumerable.Range(start, end - start).ToList(), start);
 }

 private List<T> Shuffle<T>(List<T> items) {
  var list = items.ToList();
  for (var i = list.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
  return list;
 }
 private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
 private static bool Contains(string text, string part) => part.Length >= 3 && text.Contains(part, StringComparison.OrdinalIgnoreCase);
}
