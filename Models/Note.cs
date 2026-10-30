using System.ComponentModel.DataAnnotations;
namespace ABAPKnowledgeBase.Models;
public class Note {
 public int Id {get;set;}
 [Required(ErrorMessage="Başlık gerekli")][StringLength(180)] public string Title {get;set;}="";
 [Required(ErrorMessage="Not içeriği gerekli")] public string Body {get;set;}="";
public string? Code { get; set; }
public string? Tags { get; set; }
 public int CategoryId {get;set;}
 public Category? Category {get;set;}
 // Sadece "Projeler" kategorisindeki notlarda: notun ait olduğu firma.
 public int? CompanyId {get;set;}
 public Company? Company {get;set;}
 public string? SourceFile {get;set;}
 public int? SourcePage {get;set;}
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
 public DateTime UpdatedAt {get;set;}=DateTime.UtcNow;
 // Kategori içindeki elle verilen sıra (sayfa listesinde sürükle-bırak); küçük olan üstte.
 public int SortOrder {get;set;}
 public ICollection<NoteImage> Images {get;set;}=new List<NoteImage>();
}
// Nottaki resim. Body içinde "[resim:Order]" satırıyla yerine konur.
public class NoteImage {
 public int Id {get;set;}
 public int NoteId {get;set;}
 public Note? Note {get;set;}
 public int Order {get;set;}
 [StringLength(50)] public string ContentType {get;set;}="image/png";
 public byte[] Data {get;set;}=[];
 // OneNote'un resimden okuduğu metin; aramada kullanılır.
 public string? OcrText {get;set;}
}
public class Category { public int Id {get;set;} public string Name {get;set;}=""; public ICollection<Note> Notes {get;set;}=new List<Note>(); }
// Projeler kategorisinin alt alanı: firmaya özel sayfalar burada toplanır.
public class Company {
 public int Id {get;set;}
 [Required(ErrorMessage="Firma adı gerekli")][StringLength(100)] public string Name {get;set;}="";
 public ICollection<Note> Notes {get;set;}=new List<Note>();
}
