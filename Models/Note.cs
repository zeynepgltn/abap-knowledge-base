using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ABAPKnowledgeBase.Models;
public class Note {
 public int Id {get;set;}
 [Required(ErrorMessage="Başlık gerekli")][StringLength(180)] public string Title {get;set;}="";
 [Required(ErrorMessage="Not içeriği gerekli")] public string Body {get;set;}="";
public string? Code { get; set; }
 public int CategoryId {get;set;}
 public Category? Category {get;set;}
 // Sadece "Projeler" kategorisindeki notlarda: notun ait olduğu firma.
 public int? CompanyId {get;set;}
 public Company? Company {get;set;}
 // Notun geldiği yer (OneNote bölümü veya dosya) ve oradaki sayfa numarası.
 public int? SourceId {get;set;}
 public Source? Source {get;set;}
 public int? SourcePage {get;set;}
 public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
 public DateTime UpdatedAt {get;set;}=DateTime.UtcNow;
 // Kategori içindeki elle verilen sıra (sayfa listesinde sürükle-bırak); küçük olan üstte.
 public int SortOrder {get;set;}
 public ICollection<NoteImage> Images {get;set;}=new List<NoteImage>();
 public ICollection<NoteTag> NoteTags {get;set;}=new List<NoteTag>();

 // Formdaki metin kutuları: "etiket1, etiket2" ve "OneNote: defter › bölüm" / "dosya.pdf".
 // Veritabanında tutulmaz; kaydederken Tags/Sources tablolarına çevrilir (NoteLinks).
 [NotMapped][StringLength(500)] public string? TagsText {get;set;}
 [NotMapped][StringLength(400)] public string? SourceText {get;set;}
 // NoteTags yüklüyken etiket adları.
 [NotMapped] public IEnumerable<string> TagNames => NoteTags.Where(t => t.Tag is not null).Select(t => t.Tag!.Name).OrderBy(n => n);
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
// Etiket; bir not birden çok etiket alabilir, aynı etiket birçok notta kullanılır.
public class Tag {
 public int Id {get;set;}
 [StringLength(100)] public string Name {get;set;}="";
 public ICollection<NoteTag> NoteTags {get;set;}=new List<NoteTag>();
}
public class NoteTag {
 public int NoteId {get;set;}
 public Note? Note {get;set;}
 public int TagId {get;set;}
 public Tag? Tag {get;set;}
}
// Notların geldiği yer. OneNote için Name = defter, Section = bölüm yolu; dosya için Name = dosya adı.
public class Source {
 public const string OneNote = "OneNote", File = "Dosya";
 public int Id {get;set;}
 [StringLength(20)] public string Kind {get;set;}=File;
 [StringLength(200)] public string Name {get;set;}="";
 [StringLength(300)] public string? Section {get;set;}
 public ICollection<Note> Notes {get;set;}=new List<Note>();
 public override string ToString() => Kind == OneNote ? $"OneNote: {Name} › {Section}" : Name;
}
