using ABAPKnowledgeBase.Data;
using ABAPKnowledgeBase.Models;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Services;

public sealed record QuizStats(int Streak, int Answered, int Correct, int[] Boxes, int Unseen, int Due) {
 public int Accuracy => Answered == 0 ? 0 : (int)Math.Round(100.0 * Correct / Answered);
}

/// <summary>
/// Günün quiz'i: ilk açılışta 5 soru üretilip veritabanına yazılır, gün boyunca aynı quiz gösterilir.
/// Cevaplar notların Leitner kutularını günceller (aralıklı tekrar).
/// </summary>
public sealed class QuizService(NotesDbContext db, IConfiguration config, KeyTermsRunner keyTerms) {
 // Kutu → kaç gün sonra yeniden sorulur. Kutu 1: ertesi gün, kutu 5: 16 gün sonra.
 private static readonly int[] Intervals = [0, 1, 2, 4, 8, 16];
 public const int MaxBox = 5;

 public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

 public Task<QuizDay?> FindAsync(DateOnly date) => db.QuizDays
  .Include(d => d.Questions.OrderBy(q => q.Order)).ThenInclude(q => q.Note)
  .FirstOrDefaultAsync(d => d.Date == date);

 public async Task<QuizDay> GetOrCreateAsync(DateOnly date) {
  var day = await FindAsync(date);
  if (day is not null) return day;
  // Aynı gün aynı tohum: üretim tekrarlanabilir (hata ayıklarken işe yarar).
  day = new QuizDay { Date = date, Generator = RuleQuizGenerator.Name, Questions = await GenerateAsync(date, new Random(date.DayNumber)) };
  db.QuizDays.Add(day);
  try { await db.SaveChangesAsync(); }
  catch (DbUpdateException) {
   // Aynı anda iki istek quiz oluşturduysa (iki sekme) diğerininki kullanılır.
   db.ChangeTracker.Clear();
   return await FindAsync(date) ?? throw new InvalidOperationException("Günün quiz'i oluşturulamadı.");
  }
  return (await FindAsync(date))!;
 }

 // Hiç cevap verilmediyse günün sorularını yeniden üretir.
 public async Task<bool> RegenerateAsync(DateOnly date) {
  var day = await FindAsync(date);
  if (day is null || day.Questions.Any(q => q.Answered)) return false;
  db.QuizQuestions.RemoveRange(day.Questions);
  await db.SaveChangesAsync();
  day.Questions = await GenerateAsync(date, new Random());
  day.CreatedAt = DateTime.UtcNow;
  await db.SaveChangesAsync();
  return true;
 }

 // Kaydetmeden soru üretir (--quiz-onizleme için).
 public Task<List<QuizQuestion>> PreviewAsync(DateOnly date, int seed) => GenerateAsync(date, new Random(seed));

 private async Task<List<QuizQuestion>> GenerateAsync(DateOnly date, Random rng) {
  // Not eklendi/değiştiyse anahtar terimler (TF-IDF) önce Python ile yeniden hesaplanır.
  await keyTerms.RefreshIfStaleAsync(db);
  var pool = await LoadPoolAsync();
  var reviews = await db.NoteReviews.AsNoTracking().ToDictionaryAsync(r => r.NoteId);
  return new RuleQuizGenerator(rng).Generate(pool, reviews, date);
 }

 public async Task<List<NoteMaterial>> LoadPoolAsync() {
  // Projeler gibi firmaya özel notlar varsayılan olarak quiz dışında: genel bilgi değil.
  var excluded = config.GetSection("Quiz:ExcludeCategories").Get<string[]>() ?? [CategorySeed.Projects];
  var notes = await db.Notes.AsNoTracking().Include(n => n.Category)
   .Where(n => !excluded.Contains(n.Category!.Name)).ToListAsync();
  var terms = (await db.NoteKeyTerms.AsNoTracking().OrderBy(t => t.Rank).Select(t => new { t.NoteId, t.Term }).ToListAsync())
   .GroupBy(t => t.NoteId).ToDictionary(g => g.Key, g => g.Select(t => t.Term).ToList());
  return notes.Select(n => QuizMaterial.Extract(n, n.Category!.Name, terms.GetValueOrDefault(n.Id)))
   .Where(m => m.Pairs.Count > 0 || m.CodeBlocks.Count > 0 || m.KeyTerms.Count > 0).ToList();
 }

 // Cevabı kaydeder ve notun tekrar kutusunu günceller. Bir soru sadece bir kez cevaplanır.
 public async Task<QuizQuestion?> AnswerAsync(int questionId, int choice) {
  var q = await db.QuizQuestions.Include(x => x.QuizDay).ThenInclude(d => d!.Questions).FirstOrDefaultAsync(x => x.Id == questionId);
  if (q is null || q.Answered || choice < 0 || choice >= q.Choices.Count) return q;
  q.AnswerIndex = choice;
  q.AnsweredAt = DateTime.UtcNow;
  if (q.NoteId is int noteId) {
   var review = await db.NoteReviews.FindAsync(noteId);
   if (review is null) db.NoteReviews.Add(review = new NoteReview { NoteId = noteId, Box = 1 });
   if (q.IsCorrect) { review.Box = Math.Min(review.Box + 1, MaxBox); review.CorrectCount++; }
   else { review.Box = 1; review.WrongCount++; }
   // Tekrar tarihi quiz'in gününe göre hesaplanır (dünkü quiz bugün çözülse de aynı sonuç).
   review.DueDate = q.QuizDay!.Date.AddDays(Intervals[review.Box]);
   review.LastReviewedAt = DateTime.UtcNow;
  }
  if (q.QuizDay!.Questions.All(x => x.Answered)) q.QuizDay.CompletedAt = DateTime.UtcNow;
  await db.SaveChangesAsync();
  return q;
 }

 // 👍 / 👎: aynı butona tekrar basılırsa puan kalkar.
 public async Task<QuizQuestion?> RateAsync(int questionId, int rating) {
  var q = await db.QuizQuestions.FindAsync(questionId);
  if (q is null || rating is not (1 or -1)) return q;
  q.Rating = q.Rating == rating ? 0 : rating;
  await db.SaveChangesAsync();
  return q;
 }

 public async Task<QuizStats> StatsAsync(DateOnly today) {
  var completed = await db.QuizDays.Where(d => d.CompletedAt != null).Select(d => d.Date).ToListAsync();
  var set = completed.ToHashSet();
  // Seri: bugün bitmediyse dünden geriye doğru sayılır.
  var day = set.Contains(today) ? today : today.AddDays(-1);
  var streak = 0;
  while (set.Contains(day)) { streak++; day = day.AddDays(-1); }
  var answered = await db.QuizQuestions.Where(q => q.AnswerIndex != null).Select(q => new { q.AnswerIndex, q.CorrectIndex }).ToListAsync();
  var boxes = new int[MaxBox + 1];
  foreach (var g in await db.NoteReviews.GroupBy(r => r.Box).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
   if (g.Key is >= 1 and <= MaxBox) boxes[g.Key] = g.Count;
  var poolIds = (await LoadPoolAsync()).Select(m => m.Note.Id).ToList();
  var reviewed = await db.NoteReviews.Select(r => r.NoteId).ToListAsync();
  var due = await db.NoteReviews.CountAsync(r => r.DueDate <= today);
  return new QuizStats(streak, answered.Count, answered.Count(a => a.AnswerIndex == a.CorrectIndex), boxes,
   poolIds.Except(reviewed).Count(), due);
 }
}
