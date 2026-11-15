using ABAPKnowledgeBase.Services;
using Microsoft.AspNetCore.Mvc;
namespace ABAPKnowledgeBase.Controllers;

public sealed record QuizPage(Models.QuizDay Day, QuizStats Stats);

// Günün quiz'i: /Quiz. Şıklar form olarak gönderilir; sayfa cevaplanan soruya geri kayar.
public class QuizController(QuizService quiz) : Controller {
 public async Task<IActionResult> Index() {
  var today = QuizService.Today;
  var day = await quiz.GetOrCreateAsync(today);
  return View(new QuizPage(day, await quiz.StatsAsync(today)));
 }

 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Answer(int id, int choice) {
  var q = await quiz.AnswerAsync(id, choice);
  return q is null ? NotFound() : Back(q.Order);
 }

 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Rate(int id, int rating) {
  var q = await quiz.RateAsync(id, rating);
  return q is null ? NotFound() : Back(q.Order);
 }

 [HttpPost, ValidateAntiForgeryToken]
 public async Task<IActionResult> Regenerate() {
  TempData["Flash"] = await quiz.RegenerateAsync(QuizService.Today)
   ? "Yeni sorular üretildi."
   : "Cevaplamaya başladığın quiz yenilenemez.";
  return RedirectToAction(nameof(Index));
 }

 private RedirectResult Back(int order) => Redirect(Url.Action(nameof(Index)) + "#q" + order);
}
