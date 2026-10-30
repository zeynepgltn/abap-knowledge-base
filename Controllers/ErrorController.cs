using Microsoft.AspNetCore.Mvc;
namespace ABAPKnowledgeBase.Controllers;

// Beklenmeyen hatalar (/Error) ve 404 gibi durum kodları (/Error/404) için tek sayfa.
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorController : Controller {
 [Route("Error/{code:int?}")]
 public IActionResult Index(int? code) {
  var status = code ?? 500;
  Response.StatusCode = status;
  ViewBag.Status = status;
  ViewBag.Message = status switch {
   404 => "Aradığın not veya sayfa bulunamadı. Silinmiş ya da adres hatalı olabilir.",
   400 => "İstek geçersiz. Sayfayı yenileyip tekrar dene.",
   _ => "Beklenmeyen bir hata oluştu. Ayrıntılar uygulamanın konsol çıktısında."
  };
  return View("Error");
 }
}
