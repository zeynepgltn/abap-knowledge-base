using System.Diagnostics;
using ABAPKnowledgeBase.Data;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Quiz aşama 2: notların TF-IDF anahtar terimlerini Python betiğine (ml/anahtar_terimler.py) hesaplatır.
/// Quiz üretilmeden önce çağrılır; terimler hiç hesaplanmadıysa ya da sonrasında not eklenip değiştiyse
/// betiği çalıştırır. Python ortamı (ml/.venv) kurulu değilse sessizce atlar: quiz terimler olmadan da çalışır.
/// </summary>
public sealed class KeyTermsRunner(IConfiguration config, IWebHostEnvironment env, ILogger<KeyTermsRunner> log) {
 private static readonly SemaphoreSlim Gate = new(1, 1);

 private string Script => Path.Combine(env.ContentRootPath, "ml", "anahtar_terimler.py");
 private string? Python {
  get {
   var venv = Path.Combine(env.ContentRootPath, "ml", ".venv");
   foreach (var p in new[] { Path.Combine(venv, "Scripts", "python.exe"), Path.Combine(venv, "bin", "python") })
    if (File.Exists(p)) return p;
   return null;
  }
 }

 public async Task RefreshIfStaleAsync(NotesDbContext db) {
  if (Python is not { } python || !File.Exists(Script)) return;
  await Gate.WaitAsync();
  try {
   var computed = await db.NoteKeyTerms.MaxAsync(t => (DateTime?)t.ComputedAt);
   if (computed is not null && !await db.Notes.AnyAsync(n => n.UpdatedAt > computed)) return;
   await RunAsync(python);
  } catch (Exception ex) {
   log.LogWarning(ex, "Anahtar terimler hesaplanamadı; quiz eski terimlerle hazırlanacak.");
  } finally { Gate.Release(); }
 }

 private async Task RunAsync(string python) {
  var psi = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
  psi.ArgumentList.Add(Script);
  psi.ArgumentList.Add("--db");
  psi.ArgumentList.Add(SqliteSetup.DatabasePath(config));
  psi.Environment["PYTHONIOENCODING"] = "utf-8";
  using var process = Process.Start(psi) ?? throw new InvalidOperationException("Python başlatılamadı.");
  var output = process.StandardOutput.ReadToEndAsync();
  var error = process.StandardError.ReadToEndAsync();
  using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
  try { await process.WaitForExitAsync(timeout.Token); }
  catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Anahtar terim betiği 2 dakikada bitmedi."); }
  if (process.ExitCode != 0) throw new InvalidOperationException($"Betik hata verdi ({process.ExitCode}): {await error}");
  log.LogInformation("Anahtar terimler güncellendi: {Output}", (await output).Trim());
 }
}
