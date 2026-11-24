# ABAP Bilgi Bankası: tasarım

## Brief
- **Konu:** Bir ABAP geliştiricisinin kişisel, Türkçe bilgi bankası. 400 not, 11 kategori, notlardan üretilen günlük quiz (Leitner kutuları K1–K5) ve sağda yapışkan notlar.
- **Kitle:** Tek kullanıcı, kendisi. Gün içinde SAP GUI ile bu uygulama arasında gidip gelen biri.
- **İşi:** Bir şeyi üç saniyede bulmak, okumak, düzeltmek, quiz ile tekrar etmek.
- **Kısıtlar:** ASP.NET Core MVC + Razor, düz CSS, düz JS. Yerel çalışır, internete bağımlı olmamalı (fontlar uygulamanın içinde). Açık ve koyu tema ikisi de var ve ikisi de tasarlanmalı.
- **Varsayım:** Kategori kodları (ZALV, ZSQL...) uydurma değil, kategorilerin kısa adıdır; kullanıcı istediği zaman değiştirebilir (Services/TCodes.cs).

## Konseptler
| | Fikir | Dayanak | İmza an | O/U/Y/T |
|---|---|---|---|---|
| A. Meslek malzemesi | Uygulama iyi kurulmuş bir SAP oturumu gibi davranır: üstte komut alanı, altta durum çubuğu, her kategori bir Z-işlem kodu | SAP kullanıcıları işlem koduyla gezinir (SE38, SM30) ve sonucu alttaki durum çubuğunda S/W/E mesajı olarak okur | Komut alanına `ZALV` yazıp Enter: ekran değişir, durum çubuğu "S  ALV ve Ekran İşlemleri açıldı" der | 4 / 5 / 4 / 2 |
| B. Süreç | Notlar transport yolculuğu yapar: öğrendikçe DEV → QAS → PRD | Transport sistemi + Leitner kutuları | Quiz'de doğru cevapta not bir sisteme "taşınır" | 4 / 3 / 3 / 2 |
| C. Biçimsel | Bütün arayüz klasik ABAP liste çıktısı (WRITE, ULINE, TOP-OF-PAGE) gibi dizilir | ABAP'ın adı: rapor hazırlama işlemcisi | Sayfalar liste çıktısı gibi satır satır basılır | 3 / 4 / 4 / 3 |

**Seçilen: A.** Konuya en iyi oturan ve gerçekten işe yarayan fikir bu: komut alanı bir süs değil, en hızlı gezinme yolu. C'den 404 alındı: ST22 kısa döküm sayfası.

## Konsept kartı
```
Fikir:        Bilgi bankası bir SAP oturumu gibi çalışır: işlem koduyla gidilir, sonuç durum çubuğunda okunur.
Metafor:      SAP GUI'nin komut alanı (OK-code), durum çubuğu ve Z-işlem kodları.
İmza:         Başlıktaki komut alanı. "/" ile odaklanır, ZALV / ZQUIZ / ZYENI ya da serbest arama kabul eder;
              Enter'da ekran kısa bir "dynpro değişimi" ile değişir ve durum çubuğu mesajı alttan oturur.
Duygu:        Tanıdık ve hızlı; kendi sistemim.
Hareket fiili: "Ekran değişir gibi" kısa, net, aşağıdan yukarı oturan geçişler. Yaylanma yok, süzülme yok.
Ekran görüntüsü: Ana sayfadaki büyük komut alanı ve altında Z-kodlu kategori indeksi.
```

## Sistem

### Renk
Kaynak: SAP GUI'nin gri-mavi ekranı ve durum çubuğunun S/W/E simgeleri. Mor-mavi gradyan yok, tek düz renkler.

| Rol | Açık | Koyu |
|---|---|---|
| ground (zemin) | #eef0f2 | #111418 |
| surface (ekran) | #ffffff | #191d22 |
| ink (metin) | #14181d | #e6e9ed |
| muted | #555d68 | #9aa3ae |
| line | #d5dae0 | #2c323a |
| accent (dynpro mavisi) | #1747a6 | #8fb0ff |
| S / W / E | #1e7a3c / #9a5b00 / #b42318 | #6fd39a / #f0b65a / #ff8a80 |

Kontrast hedefi AA. muted açık zeminde 6.4:1, koyu zeminde 7.1:1.

### Tipografi
- **Host Grotesk** (300–800, değişken): başlıklar ve arayüz. Gösterim boyutunda 750, harf aralığı -0.035em.
- **JetBrains Mono** (400–700): işlem kodları, kod blokları, durum çubuğu, sayılar. Kod bu uygulamanın asıl içeriği, mono bir süs değil.
- Ölçek: `--t-display clamp(2.6rem, 1.4rem + 4.6vw, 5.25rem)`, `--t-h1 clamp(1.9rem, 1.4rem + 1.8vw, 2.9rem)`, `--t-h2 1.25rem`, gövde 16px / 1.6, arayüz 13–14px.

### Izgara ve ritim
- İçerik 1240px, okuma ekranında 1640px. Kenar boşluğu 24px, telefonda 16px.
- Okuma ekranı üç kolon: kategoriler 240 / sayfalar 290 / belge. Telefonda tek kolon.
- Bölüm aralığı ana sayfada 56 / 32 / 24, iç sayfalarda daha sıkı.

### Biçim
Tek köşe kuralı: 6px (alanlar, butonlar, kartlar). Kod ve işlem kodu rozetleri 4px. Hap şekli yok.

### Hareket dili
- Tek eğri: `cubic-bezier(.2, .7, .1, 1)`. Küçük şeyler 140ms, ekran değişimi 220ms.
- **Ana butonlar:** Enter tuşu fikri. Üzerine gelince sağdan "↵" kayar, etiket 4px sola çekilir. Komutu çalıştırmak.
- **Liste satırları (kategori, sayfa):** İşlem kodu rozeti dolup ters döner, satır içeriği 4px sağa kayar.
- **Üst menü bağlantıları:** Alt çizgi soldan çizilir.
- **Sayfa geçişi:** View Transitions API. Başlık, durum çubuğu ve yapışkan sütun sabit kalır, sadece `main` değişir: eskisi 6px yukarı solar, yenisi 8px aşağıdan oturur.
- **Boşta hareket:** Komut alanındaki blok imleç yanıp söner (SAP'de imleç alanda bekler). Başka hiçbir şey kendiliğinden kıpırdamaz. Bu bir araç, sakin durmalı. Azaltılmış harekette imleç sabit.
- Yükleme ekranı yok, WebGL yok: tek kullanıcılı bir araçta tiyatro değil hız önemli.

### Kompozisyon
```
ANA SAYFA
┌ ◈ ABAP Bilgi Bankası   [ /  komut veya arama ▮      ]   Notlar  Quiz  [+ Yeni not ↵]  ☾  📌 ┐
│                                                                                            │
│  Ne açalım?                                   (display, sol hizalı)                        │
│  [ ZALV, REDUCE, ?= ...                                            ] [Çalıştır ↵]          │
│  ZQUIZ günün quiz'i · ZYENI yeni not · / her yerden odaklan                                │
│                                                                                            │
│  Kategoriler                                                   400 not                     │
│  ZTEMEL   ABAP Temelleri ................................ 34                               │
│  ZITAB    Internal Table ve Modern ABAP ................. 16   (indeks tablosu, kart değil)  │
│  ...                                                                                       │
└ S  Hazır.                                                  ZNOT · 400 not · 11 kategori ───┘

OKUMA EKRANI: başlıkta kategori adı + kod rozeti; üç kolon; belge yüzeyi en geniş.
QUIZ: başlık + kutular (K1–K5) tek şerit tablo; soru kartları tek kolon 820px.
404/500: ST22 kısa döküm: "Çalışma zamanı hatası  SAYFA_BULUNAMADI", Ne oldu? / Ne yapabilirsin? / Hata analizi.
```

### Yapışkan notlar (sağ sütun)
SAP'nin içinde olmayan tek şey: kişinin kendi karalamaları. Bu yüzden sistemin malzemesiyle (beyaz yüzey, 6px köşe, mono meta) ama kendi rengiyle durur.
- Kâğıt rengi kartı boyamaz: solda 4px renk şeridi, yüzeyde %8 ton. Pastel bloklar sayfadaki en baskın şey olmasın.
- Kartın başı tek satır: ⠿ tutamaç, mono tarih/durum ("Kaydedildi"), renk noktası, 🖼, ✓, ×. Hepsi 28px.
- Renk noktası altı renklik seçiciyi açar (24px noktalar), seçince kapanır. Eski 10px noktalar telefonda basılamıyordu.
- Tamamlananlar altta "Tamamlananlar" başlığıyla ayrılır, soluk ve üstü çizili; üzerine gelince tam görünür.
- Başlıkta açık not sayısı (mono rozet), dolu mavi + düğmesi (yeni kayıt) ve ⇥.
- Hareket: yeni not yukarıdan 10px oturur, silinen sağa çekilerek kaybolur, + döner, ⇥ kapanma yönüne kayar.
- Boş not birikmez: + ile açılan not yazı ya da resim almadan odak dışına çıkarsa kendiliğinden silinir.
- Sadece resim olan notta yer tutucu "Açıklama ekle…" olur ve tek satıra iner.
- Dar ekranda panel sayfanın üstüne perdeyle açılır; dışarı dokununca, Esc ile ya da ⇥ ile kapanır.
- İkonlar emoji değil, 1.5px çizgili SVG (resim ikonu); 📌 sadece üst menüdeki etiket.

```
┌ Yapışkan notlar [3]          (+) ⇥ ┐
│ ┃ ⠿ 18 Kas            ● 🖼 ✓ ×    │
│ ┃ Sd maddeleri                     │
│ ┃ [ resim ]                         │
│                                    │
│ Tamamlananlar                      │
│ ┃ ⠿ 17 Kas  (soluk, üstü çizili)   │
└────────────────────────────────────┘
```

### Alt kategoriler
- Her kategori alt kategorilere bölünebilir (ALV › CL GUI ALV, REUSE ALV, SALV...). Alt kategorisi olmayan not "Genel"dedir.
- Sayfa listesinde alt kategori başlıkları yapışık durur, yanında mono sayı rozeti. Sayfa başka bir başlığın altına sürüklenince o alt kategoriye geçer.
- "Alt kategorilere böl" ekranında notlar elle yerleştirilir; Kaydet demeden hiçbir şey yazılmaz.

### Genellik testi
- Kategori kartları ızgarası (her SaaS paneli) yerine Z-kodlu indeks tablosu.
- Ortada arama kutulu kahraman alanı yerine sola yaslı, komut alanı olan "Ne açalım?" ekranı. Kutu gerçekten komut çalıştırıyor.
- Yeşil "kaydedildi" kutusu yerine durum çubuğu mesajı.
- Hazır 404 yerine kısa döküm.
