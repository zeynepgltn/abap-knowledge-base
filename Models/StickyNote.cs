using System.ComponentModel.DataAnnotations;
namespace ABAPKnowledgeBase.Models;

// Yapışkan not (post-it): ABAP notlarından ayrı, kısa aktivite / yapılacak notları için pano.
// Notlar veritabanında durur; yedekle diğer bilgisayara da geçer.
public class StickyNote {
 public int Id {get;set;}
 [StringLength(4000)] public string Text {get;set;}="";
 // Kâğıt rengi: StickyColors içinden biri.
 [StringLength(20)] public string Color {get;set;}=StickyColors.Yellow;
 // Tamamlanan not soluk görünür ve panonun sonuna iner.
 public bool Done {get;set;}
 // Panodaki sıra (sürükle-bırak); küçük olan önde.
 public int SortOrder {get;set;}
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
 public DateTime UpdatedAt {get;set;}=DateTime.UtcNow;
 // Nota eklenen resimler (yapıştır, sürükle-bırak veya 🖼). Not silinince resimleri de silinir.
 public ICollection<StickyImage> Images {get;set;}=new List<StickyImage>();
 // Panoda gösterilecek resimlerin id'leri (resim verisi yüklenmeden; bkz. StickyDockViewComponent).
 [System.ComponentModel.DataAnnotations.Schema.NotMapped] public List<int> ImageIds {get;set;}=[];
}

public class StickyImage {
 public int Id {get;set;}
 public int StickyNoteId {get;set;}
 public StickyNote? StickyNote {get;set;}
 [StringLength(50)] public string ContentType {get;set;}="image/png";
 public byte[] Data {get;set;}=[];
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
}

public static class StickyColors {
 public const string Yellow = "sari";
 public static readonly string[] All = [Yellow, "pembe", "yesil", "mavi", "mor", "turuncu"];
 public static string Label(string color) => color switch {
  "pembe" => "Pembe", "yesil" => "Yeşil", "mavi" => "Mavi", "mor" => "Mor", "turuncu" => "Turuncu", _ => "Sarı" };
}
