# ABAP Bilgi Bankası (127.0.0.1:4400): jüri puanı
Tahmindir, Awwwards ile bağlantısı yoktur. Bağımsız değerlendirici (kodu ve amacı görmedi), 4 sayfa (ana sayfa, Günün Quiz'i, Yeni Not formu, 404), 18.11.2026.
Release derlemesi, veritabanının kopyası üzerinde ölçüldü.

> Not: Bu, internete açık bir tanıtım sitesi değil, tek kullanıcılı yerel bir bilgi bankası aracı. Awwwards ölçütleri vitrin sitelerine göre kurulu.
> SEO, meta ve sosyal paylaşım puanları bu yüzden bu uygulama için daha az anlamlı. Asıl değerli kısım Tasarım, Kullanılabilirlik ve Erişilebilirlik bulguları.
> Kayıtlı bir notun detay sayfası taranmadı (tarayıcı not türü olarak Yeni Not formunu aldı).

**Değerlendiricinin okuduğu fikir:** Notları kategorilere ayıran, kendi notlarından K1–K5 kutulu (aralıklı tekrar) günlük quiz üreten ve yanında yapışkan not paneli olan kişisel, Türkçe bir ABAP/SAP bilgi bankası.
**Değerlendirici genel puanı: 5,6** (0,4 Tasarım + 0,3 Kullanılabilirlik + 0,2 Yaratıcılık + 0,1 İçerik, değerlendiricinin katı ölçeğinde)
**Jüri tahmini: yaklaşık 6,7** (değerlendirici + 1,1; fark iki yakın tarihli Günün Sitesi ödülüyle ölçüldü, zayıf sitelerde ölçülmedi)
**Bant (tahminden):** Mansiyon aralığı (6,5–7,1)

| Ölçüt | Puan | Kanıt |
|---|---|---|
| Tasarım (%40) | 5 | home/d-01-hero: düzenli kart ızgarası, tutarlı ama hazır yönetim paneli görünümünde. Her m-01'de başlığın ikinci satırı alt çizgiden taşıyor (home/m-03: içerik başlığın altına kayıyor). Quiz/d-01'de h1 başlığa 28 px yakın (diğer sayfalarda ~90 px). Odak çerçevesi metni kesiyor (home/k-04). |
| Kullanılabilirlik (%30) | 6 | Hızlı, 60 fps, azaltılmış hareket tam. Ama telefonda "Notlarım" kayboluyor (home/m-01), başlık içeriğin üstüne biniyor, her sayfada kontrast hatası var, atlama bağlantısı yok. |
| Yaratıcılık (%20) | 6 | Quiz/d-01: kendi notlarından üretilen, Leitner kutulu, 👍/👎 ile puanlanan quiz konuya özgü bir fikir. Ama fikir mantıkta kalıyor, ekranda imza niteliğinde bir an yok. |
| İçerik (%10) | 6 | Gerçek ve özgün içerik: 400 not, gerçek ABAP kodu soruları (Quiz/d-07). Ama özel bir karalama notu 404 dahil her sayfada en belirgin metin (404/d-01). Quiz/d-04'te kesik şık ("tabloya..."). |

| Geliştirici | Puan | Kanıt |
|---|---|---|
| Semantik/SEO | 4 | audit: 3 FAIL (meta description yok), canonical/robots/sitemap yok, dört sayfada da aynı `<title>`. Her sayfada tek h1 var. |
| Animasyon/Geçiş | 4 | d-90 a/b/c karelerinin hepsi d-91 ile aynı: sayfalar arasında geçiş yok. `colorOnly` 4–6: "Yapışkan Notlar" ve "⇥" yalnızca renk değiştiriyor. En iyi hover efekti Quiz/h-04'teki cevap satırı. |
| Erişilebilirlik | 5 | `axeSerious 2` (renk kontrastı ×4–29, `.nav-sticky` için label-content-name-mismatch), atlama bağlantısı yok, 404'te yapışkan textarea ve renk seçicide odak görünmüyor. |
| WPO | 8 | Kısıtlı telefonda LCP 1,1–1,9 sn, CLS 0, yükleme ekranı yok, en ağır sayfa ~17 KB. Ana sayfada bir kaynak 404 dönüyor (favicon). |
| Duyarlı tasarım | 5 | Yatay taşma yok, ama sayfa başına 8 küçük dokunma hedefi (12×12 renk noktaları, 22 px menü bağlantıları). Başlık telefonda bozuk, Quiz istatistikleri telefonda 3+1+1 diziliyor. |
| İşaretleme/Meta | 3 | og:*, twitter:card ve favicon bağlantısı yok, meta description FAIL ×3. |

## Düzeltmeler, en çok puan kazandırandan başlayarak
1. [Duyarlı/Kullanılabilirlik/Tasarım] m-01 (tüm sayfalar): telefonda başlığın ikinci satırı alt çizgiden taşıyor ve "Notlarım" kayboluyor. 390 px'te başlığı tek satır yap (yalnızca ◈ logosu, ardından en az 44 px yüksekliğinde ikon butonlar), ya da `height:auto` verip alt çizgiyi ikinci satırın altına al.
2. [Erişilebilirlik] Kontrast ×27–29: `.muted` rengini #85818b'den ~#6a6670'e, `.category-icon` metnini #a15b77'den ~#8a4864'e koyulaştır.
3. [Erişilebilirlik] `.nav-sticky`: aria-label görünen "Yapışkan Notlar" metniyle başlasın ya da aria-label kaldırılsın.
4. [Meta] Her sayfaya ayrı `<title>` ve meta description ekle. Favicon ekle (konsoldaki 404 hatası da gider).
5. [Animasyon/Tasarım] Hover: ana butonlar 1–2 px yükselsin ve gölgesi koyulaşsın, menü bağlantılarında alt çizgi soldan çizilsin. Quiz/h-04'teki etki doğru kuvvette.
6. [Animasyon] Sayfa geçişi: `@view-transition { navigation: auto; }` ile `main` üzerinde 200 ms kayan bir solma efekti.
7. [Duyarlı] Renk noktalarına dolgu verip 24×24 tıklama alanı aç, telefonda menü bağlantıları en az 44 px olsun.
8. [Erişilebilirlik] İlk Tab durağı "İçeriğe geç" atlama bağlantısı olsun. Odak çerçevesi 2 px mauve `:focus-visible` olsun, 3 px boşluk bıraksın, yapışkan textarea ve renk seçicide de görünsün.
9. [Tasarım] Quiz: üst boşluğu diğer sayfalarla eşitle, masaüstünde sütunu panele kadar genişlet, telefonda istatistikleri 2 sütuna diz.
10. [İçerik] Yapışkan panel ana sayfa dışında kapalı başlasın ya da boş bir durum göstersin.
11. [İçerik] Quiz şıkları: çeldiricileri cümle sınırında kes, doğru cevapla aynı nottan gelen çeldiricileri ele.
12. [Tasarım] Ana sayfada iki "+ Yeni Not" var: birini kaldır ya da sadeleştir.
13. [SEO] robots.txt ve sitemap ekle (yerel prototip için düşük değerli).

## Ölçülenler
Telefonda LCP 1056–1872 ms, CLS 0, yükleme ekranı yok, masaüstü ve telefonda 60 fps. Ciddi axe ihlali sayfa başına 2 kural (renk kontrastı ×4–29, label-content-name-mismatch ×1). Yatay taşma yok. `noHoverChange` 0, `colorOnly` 4–6, boşta hareket yok, atlama bağlantısı yok, 404'te `noVisibleFocus` 2. Audit: 3 FAIL, 20 WARN.

Puanlanmayanlar: durağan görüntülerin gösteremediği her şey (hareketin hissi, ses).
