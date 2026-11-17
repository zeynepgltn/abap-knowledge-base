using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
namespace ABAPKnowledgeBase.Models;

// Günlük quiz: her gün 5 soru (3 mantık + 2 kod; 1 kolay + 3 orta + 1 zor).
// Sorular veritabanında tutulur: gün içinde aynı quiz açılır, yedekle diğer bilgisayara da geçer.
// Soruları kim üretirse üretsin (şimdi kural tabanlı C#, ileride Python/ML) bu tablolara yazar.
public class QuizDay {
 public int Id {get;set;}
 public DateOnly Date {get;set;}
 // Soruları üreten yöntem ve sürümü, ör. "kural-v1". Hangi yöntemin daha iyi soru ürettiğini ölçmek için.
 [StringLength(40)] public string Generator {get;set;}="";
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
 // Son soru cevaplanınca dolar; seri (art arda günler) bundan hesaplanır.
 public DateTime? CompletedAt {get;set;}
 public List<QuizQuestion> Questions {get;set;}=new();
}

public class QuizQuestion {
 public int Id {get;set;}
 public int QuizDayId {get;set;}
 public QuizDay? QuizDay {get;set;}
 // Quiz içindeki sıra (1-5).
 public int Order {get;set;}
 // Sorunun dayandığı not. Not silinirse soru kalır, bağlantı boşalır.
 public int? NoteId {get;set;}
 public Note? Note {get;set;}
 [StringLength(10)] public string Kind {get;set;}=QuizKind.Logic;
 [StringLength(10)] public string Difficulty {get;set;}=QuizDifficulty.Medium;
 // Soru tipi, ör. "kavram-bul", "tanim-sec", "bosluk-doldur", "farkli-olan", "kod-ne-yapar", "kod-tamamla", "hatali-satir".
 [StringLength(30)] public string Type {get;set;}="";
 public string Prompt {get;set;}="";
 // Soruyla birlikte gösterilecek kod (varsa).
 public string? Code {get;set;}
 // Şıklar JSON dizi olarak: ["a","b","c","d"].
 public string ChoicesJson {get;set;}="[]";
 public int CorrectIndex {get;set;}
 public string? Explanation {get;set;}
 public int? AnswerIndex {get;set;}
 public DateTime? AnsweredAt {get;set;}
 // Kullanıcının soru puanı: 1 = iyi soru, -1 = kötü soru, 0 = puanlanmadı. İleride ML için veri seti.
 public int Rating {get;set;}

 [NotMapped] public List<string> Choices {
  get => JsonSerializer.Deserialize<List<string>>(ChoicesJson) ?? [];
  set => ChoicesJson = JsonSerializer.Serialize(value);
 }
 [NotMapped] public bool Answered => AnswerIndex.HasValue;
 [NotMapped] public bool IsCorrect => AnswerIndex == CorrectIndex;
}

// Notun aralıklı tekrar durumu (Leitner kutuları). Doğru cevap notu bir üst kutuya taşır ve
// daha seyrek sorulmasını sağlar; yanlış cevap 1. kutuya geri gönderir.
public class NoteReview {
 [Key] public int NoteId {get;set;}
 public Note? Note {get;set;}
 public int Box {get;set;}=1;
 // Bu tarihten itibaren not yeniden sorulabilir.
 public DateOnly DueDate {get;set;}
 public DateTime? LastReviewedAt {get;set;}
 public int CorrectCount {get;set;}
 public int WrongCount {get;set;}
}

public static class QuizKind { public const string Logic = "Mantık", Code = "Kod"; }
public static class QuizDifficulty { public const string Easy = "Kolay", Medium = "Orta", Hard = "Zor"; }

// Notun anahtar terimleri (quiz aşama 2). Python betiği ml/anahtar_terimler.py TF-IDF ile hesaplayıp yazar;
// C# sadece okur. Rank 1 = notu en iyi ayırt eden terim. Notlar değişince yeniden hesaplanır.
public class NoteKeyTerm {
 public int NoteId {get;set;}
 public Note? Note {get;set;}
 public int Rank {get;set;}
 [StringLength(120)] public string Term {get;set;}="";
 public double Score {get;set;}
 public DateTime ComputedAt {get;set;}
}
