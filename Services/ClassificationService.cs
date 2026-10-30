using System.Text.RegularExpressions;
namespace ABAPKnowledgeBase.Services;

/// <summary>
/// Nota ABAP anahtar kelimelerine göre kategori önerir.
/// Kelimeler tam kelime olarak eşleşir: "MOD" artık MODULE/MODIFY içinde yakalanmaz,
/// "SELECT" de SELECT-OPTIONS içinde yakalanmaz.
/// Ağırlık: 3 = o kategoriye özgü terim, 1 = başka yerlerde de geçebilen genel terim.
/// </summary>
public class ClassificationService {
 private sealed record Rule(string Category, (string Term, int Weight)[] Terms);

 private static readonly Rule[] Rules = [
  new("ALV ve Ekran İşlemleri", [
   ("CL_GUI_ALV_GRID",3),("CL_SALV_TABLE",3),("SET_TABLE_FOR_FIRST_DISPLAY",3),("REFRESH_TABLE_DISPLAY",3),
   ("LVC_T_FCAT",3),("LVC_S_LAYO",3),("FIELDCATALOG",3),("FIELDCAT",3),("REUSE_ALV",3),("CL_GUI_DOCKING_CONTAINER",3),
   ("CL_GUI_CUSTOM_CONTAINER",3),("HOTSPOT_CLICK",3),("SELECTION-SCREEN",1),("PF-STATUS",3),("CALL SCREEN",3),
   ("PBO",3),("PAI",3),("USER_COMMAND",1),("SALV",1),("ALV",1)]),
  new("Internal Table ve Modern ABAP", [
   ("VALUE #(",3),("REDUCE",3),("CORRESPONDING #(",3),("FILTER #(",3),("COND #(",3),("SWITCH #(",3),("CONV #(",3),
   ("FIELD-SYMBOLS",3),("FIELD-SYMBOL(",3),("READ TABLE",3),("LOOP AT GROUP",3),("COLLECT",3),("SORTED TABLE",3),
   ("HASHED TABLE",3),("TABLE_LINE",3),("LINE_EXISTS",3),("APPEND",1),("MODIFY",1),("INTERNAL TABLE",1)]),
  new("SQL, CDS ve Dictionary", [
   ("FOR ALL ENTRIES",3),("INNER JOIN",3),("LEFT OUTER JOIN",3),("GROUP BY",3),("ORDER BY",1),("UP TO",1),
   ("DEFINE VIEW",3),("CDS",3),("SE11",3),("SE16",3),("SE16N",3),("DATA ELEMENT",3),("DOMAIN",1),
   ("LOCK OBJECT",3),("ENQUEUE_",3),("OPEN SQL",3),("SELECT SINGLE",3),("ENDSELECT",3),("SELECT",1)]),
  new("Nesne Yönelimli ABAP", [
   ("?=",3),("CAST #(",3),("NEW #(",3),("CL_ABAP_TYPEDESCR",3),("CL_ABAP_STRUCTDESCR",3),("RTTI",3),("RTTS",3),
   ("INHERITING FROM",3),("INTERFACES",3),("REDEFINITION",3),("CREATE OBJECT",3),("SINGLETON",3),("CLASS-METHODS",3),
   ("CLASS-DATA",3),("SE24",3),("ENDCLASS",3),("REF TO",1),("METHOD",1),("INTERFACE",1),("CLASS",1)]),
  new("Fonksiyonlar, BAPI ve Entegrasyon", [
   ("BAPI_",3),("BAPIRET2",3),("BAPI",3),("CALL FUNCTION",2),("RFC",3),("SM59",3),("SE37",3),("IDOC",3),
   ("CL_HTTP_CLIENT",3),("HTTP_CLIENT",3),("REST",1),("JSON",3),("ODATA",3),("FUNCTION MODULE",3),("DESTINATION",1)]),
  new("Forms ve Yazdırma", [
   ("SMARTFORM",3),("SMARTFORMS",3),("SSF_FUNCTION_MODULE_NAME",3),("FP_FUNCTION_MODULE_NAME",3),("ADOBE FORM",3),
   ("ADOBE",1),("SFP",3),("NAST",3),("NACE",3),("FP_JOB_OPEN",3),("SPOOL",3),("SAPSCRIPT",3),("SE71",3),
   ("FORM INTERFACE",3),("SMARTSTYLE",3),("FORMCALC",3),("SE78",3),("JAVASCRIPT",2),("LOGO",1),("YAZDIRMA",1),("PRINT",1)]),
  new("Excel, Dosya ve Mail", [
   ("EXCEL",3),("XLSX",3),("GUI_DOWNLOAD",3),("GUI_UPLOAD",3),("CL_BCS",3),("SO_NEW_DOCUMENT",3),
   ("TEXT_CONVERT_XLS_TO_SAP",3),("ALSM_EXCEL_TO_INTERNAL_TABLE",3),("CL_GUI_FRONTEND_SERVICES",3),
   ("SPREADSHEET",3),("ABAP2XLSX",3),("CSV",3),("OPEN DATASET",3),("AL11",3),("MAIL",1),("DOSYA",1)]),
  new("Debug ve Hatalar", [
   ("BREAK-POINT",3),("WATCHPOINT",3),("BREAKPOINT",3),("ST22",3),("SHORT DUMP",3),("DUMP",3),("CX_SY_",3),
   ("CX_ROOT",3),("TRY",1),("CATCH",1),("DEBUG",3),("DEBUGGING",3),("SLG1",3),("HATA",1)]),
  new("SAP Araçları ve Transport", [
   ("TRANSPORT REQUEST",3),("TRANSPORT",3),("SE09",3),("SE10",3),("STMS",3),("SE80",3),("SE38",3),("SE93",3),
   ("SNOTE",3),("SNRO",3),("SM30",3),("SE54",3),("SPRO",3),("PAKET",1),("PACKAGE",1),("TCODE",1),("REQUEST",1)]),
  new("ABAP Temelleri", [
   ("WRITE",1),("DATA",1),("IF",1),("ENDIF",1),("CASE",1),("DO",1),("WHILE",1),("LOOP AT",1),("MOD",1),("DIV",1),
   ("CONDENSE",3),("CONCATENATE",3),("SPLIT",3),("REPLACE",1),("STRLEN",3),("TRANSLATE",3),("PARAMETERS",1),
   ("TYPES",1),("CONSTANTS",1)])
 ];

 // Her terim için önceden derlenmiş regex:
 // - Terim harf/rakam/_ ile başlıyorsa önünde kelime sınırı aranır ("?=" gibi sembollerde aranmaz).
 // - Sonda sınır sadece kısa (< 5 harf) veya genel (ağırlık 1) terimlerde aranır. Uzun özgül terimlere
 //   Türkçe ek gelebilir: "SMARTFORMDA", "EXCELDEN", "TRANSPORTU". Kısa terimler (MOD, IF, RFC)
 //   böylece MODULE, IFRS gibi kelimelerin içinde yakalanmaz.
 // - "BAPI_" ve "CX_SY_" gibi _ ile biten terimler bilinçli olarak önek gibi eşleşir.
 private static readonly (string Category, (string Term, int Weight, Regex Rx)[] Terms)[] Compiled =
  Rules.Select(r => (r.Category, r.Terms.Select(t => (t.Term, t.Weight, Build(t.Term, t.Weight))).ToArray())).ToArray();

 private static Regex Build(string term, int weight) {
  const string wordChar = "[A-Z0-9_-]";
  var start = IsWord(term[0]) ? $"(?<!{wordChar})" : "";
  var allowSuffix = weight > 1 && term.Length >= 5;
  var end = IsWord(term[^1]) && term[^1] != '_' && !allowSuffix ? $"(?!{wordChar})" : "";
  var body = Regex.Escape(term).Replace("\\ ", "\\s+"); // "READ  TABLE" de eşleşsin
  return new Regex(start + body + end, RegexOptions.CultureInvariant | RegexOptions.Compiled);
 }
 private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

 public IReadOnlyList<(string Category,int Score,string Evidence)> Suggest(string input) {
  var text = input.ToUpperInvariant();
  return Compiled.Select(rule => {
   var matched = rule.Terms.Where(t => t.Rx.IsMatch(text)).ToArray();
   // Kanıt olarak önce güçlü terimleri göster.
   var evidence = string.Join(", ", matched.OrderByDescending(t => t.Weight).Select(t => t.Term));
   return (Category: rule.Category, Score: matched.Sum(t => t.Weight), Evidence: evidence);
  })
  // Sadece genel terimlerle (toplam puan < 2) gelen öneriler gürültüdür, gösterme.
  .Where(x => x.Score >= 2)
  .OrderByDescending(x => x.Score).ThenBy(x => x.Category).Take(3).ToList();
 }
}
