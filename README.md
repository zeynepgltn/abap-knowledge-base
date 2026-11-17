# ABAP Bilgi Bankası — Kişisel Not Uygulaması

**Amaç:** Notu yaz → kategori önerisi al → elle onaylayıp kaydet → başlık/açıklama/kod/etikette ara.

ASP.NET Core MVC (.NET 10) + Entity Framework Core + SQLite. Yedekler OneDrive'a gider.

## Kurulum (Windows)

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) yükle. Başka bir şey kurmak gerekmez (veritabanı SQLite, uygulamayla birlikte gelir).
2. Terminalde bu klasöre git ve çalıştır:

   ```powershell
   dotnet restore
   dotnet run --urls "http://localhost:5168"
   ```

3. Tarayıcıda **http://localhost:5168** adresine gir. Uygulama ilk çalıştırmada veritabanını ve 11 kategoriyi (10 ABAP konusu + Projeler) oluşturur.

Veritabanı tek bir dosyadır: `%LOCALAPPDATA%\ABAPKnowledgeBase\abap-notlar.db`. Yeri değiştirilecekse `appsettings.Local.json.example` dosyasına bak. Bu dosya OneDrive klasörüne konmamalı: açıkken senkronlanırsa bozulabilir. OneDrive'a sadece yedekler gider.

> **Not verisi bu repoda yok.** Notlar, resimler, yedekler ve içe aktarma çıktıları kişisel olduğu için `.gitignore` ile dışarıda tutulur; uygulama boş bir veritabanıyla başlar.

## OneNote notlarını içe aktarma

Notlar masaüstü OneNote'tan (resimler ve resimlerin OCR metni dahil) aktarılır. OneNote'ta aktarılacak not defterleri açık olmalı.

1. Dışa aktar (her sayfa `ImportData/onenote/pages/` altına XML olarak yazılır):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\Export-OneNote.ps1
   ```

2. Önizle (veritabanına dokunmaz):

   ```powershell
   dotnet run -- --import-onenote
   ```

   `ImportData/onenote/onizleme.md` her sayfanın kategorisini ve nedenini, `onizleme-govdeler.txt` dönüştürülmüş metni gösterir. ⚠ işaretli satırlarda bölüm ile içerik çelişiyor.
   Kategoriler `ImportData/onenote-kategoriler.json` ile belirlenir (ilk seferde `onenote-kategoriler.example.json` dosyasını bu adla kopyala). Öncelik sırası: `"defter/bölüm/başlık"`, sonra `"defter/bölüm"`, sonra `"defter/*"`. Boş değer verilen veya listede olmayan sayfalar kategori önericiye bırakılır.

3. Uygula:

   ```powershell
   dotnet run -- --import-onenote --apply
   ```

   Mevcut notlar `ImportData/yedek/` altına JSON olarak yedeklenir, **veritabanı silinip yeniden kurulur** ve OneNote sayfaları eklenir.

### Sadece eksikleri ekleme

OneNote'a sonradan eklenen sayfaları, mevcut notlara dokunmadan almak için:

```powershell
dotnet run -- --import-onenote --add          # önizleme, veritabanına dokunmaz
dotnet run -- --import-onenote --add --apply  # eksikleri ekler
```

Var olan notlar, sıralamalar ve firmalar değişmez; yeni sayfalar kategorilerinin sonuna eklenir. Projeler'e giden sayfalar firmalarına eklenir; firma yoksa oluşturulur (büyük/küçük harf farkı gözetilmez). Birebir aynı başlık ve içeriğe sahip sayfalar ikinci kez eklenmez. OneNote'ta sonradan değişen sayfaların üzerine yazılmaz, önizlemede listelenir (içi boş olan notlar doldurulur). Sitede düzenlenmemiş olduklarından eminsen `--update-changed` ile OneNote'taki güncel hâlleri alınır (kategori, firma, etiket ve sıra korunur).

Eşlemede `"__ATLA__"` değeri o sayfa, bölüm veya bölüm grubunu (`"defter/grup/*"`) tamamen dışarıda bırakır. `"Projeler:Firma"` değeri sayfaları Projeler'de o firmaya ekler (ör. `"zonte/MOROCCO-FAS/*": "Projeler:MOROCCO-FAS"`); sadece `"Projeler"` yazılırsa firma OneNote bölüm adıdır.

Not gövdesinde ```` ``` ```` satırları arası kod bloğu, tek başına `[resim:N]` satırı notun N. resmi olarak gösterilir.

## Özellikler

- Not ekleme, düzenleme, silme (onaylı), listeleme ve detay görüntüleme
- Üç kolonlu okuma ekranı (kategoriler, sayfalar, not); düzenleme ve yeni not sağ panelde açılır (bir kategori/firmadayken "+ Yeni Not" o kategori ve firma seçili gelir)
- Notlara resim: "Resim ekle" ile seç, ekran görüntüsünü açıklama kutusuna yapıştır (Ctrl+V) veya sürükle-bırak; resim imlecin olduğu yere `[resim:N]` satırı olarak girer. Düzenlemede resimler önizlenir ve silinebilir (PNG/JPEG/GIF/WEBP/BMP, en fazla 10 MB)
- Etiketler isteğe bağlı: metinde geçmeyen ama aramak isteyeceğin anahtarlar için (program/Tcode adı, proje, modül). Aramada etiket eşleşmesi metinden önde sıralanır ve arama sonuçlarında etiket filtresi olur
- Projeler kategorisinde firmalara göre gruplama
- Sayfaları sürükle-bırak ile sıralama
- OneNote'tan resimleriyle aktarım; resimlerdeki yazıda (OCR) arama
- Açık / koyu tema
- 10 ABAP kategorisi + Projeler; ABAP terimlerine dayalı kategori önerisi (tam kelime eşleşmesi, ağırlıklı puan, kullanıcı onaylı)
- Teknik sembolleri de (`?=`, `VALUE #(`) arayan arama: en ilgili sonuç üstte (başlık > etiket > kod > metin > resimdeki yazı), her sonuçta vurgulu pasaj, kategori ve etiket filtreleri; açılan notta eşleşmeler işaretlenir
- Arama kabul testi: uygulama açıkken `powershell -ExecutionPolicy Bypass -File scripts\Test-Search.ps1` → `tests/arama-testleri.json`'daki sorgularda beklenen not ilk 5'te mi ölçer
- Etiketler ve kaynaklar ayrı tablolarda: bir etiket birçok notta kullanılır; kaynak OneNote bölümü (`OneNote: defter › bölüm`) veya dosya adıdır, formda metin olarak girilir
- 404 / hata durumunda uygulamanın kendi hata sayfası
- Otomatik yedek: günlük + not kaydedince + kapanışta; son 14 gün yerelde ve OneDrive'da; iki bilgisayar arasında otomatik aktarım
- Günün Quiz'i (aşağıda)

## Günün Quiz'i

Menüdeki **Günün Quiz'i** her gün notlardan 5 soru hazırlar: 3 mantık + 2 kod; 1 kolay + 3 orta + 1 zor. Ücretli servis kullanmaz, tamamen yerel çalışır.

| # | Tür · Zorluk | Soru tipi |
|---|---|---|
| 1 | Mantık · Kolay | Açıklaması verilen kavram hangisi? |
| 2 | Mantık · Orta | Kavramın doğru açıklaması hangisi? |
| 3 | Mantık · Orta | Hangisi bu notun kavramlarından biri değil? |
| 4 | Kod · Orta | Bu kod satırı ne yapar? (yoksa: boşluğa hangi anahtar kelime gelir?) |
| 5 | Kod · Zor | Kod parçasındaki hatalı satır hangisi? |

- **Sorular nereden gelir:** notlardaki `KAVRAM: açıklama` ve `KAVRAM | açıklama` satırları ile kod bloklarından, kurallarla (`Services/QuizMaterial.cs`, `Services/RuleQuizGenerator.cs`). Not ne kadar bu düzende yazılırsa sorular o kadar iyi olur.
- **Aralıklı tekrar (Leitner):** doğru cevap notu bir üst kutuya taşır (1, 2, 4, 8, 16 gün sonra yeniden sorulur), yanlış cevap 1. kutuya geri gönderir. Önce yanlış bilinenler, sonra hiç sorulmamış notlar gelir.
- **Gün boyu aynı quiz:** sorular veritabanına yazılır; yedekle diğer bilgisayara da geçer. Henüz cevap vermediysen **↻ Başka sorular** ile yenilenebilir.
- **👍 / 👎:** her cevaptan sonra sorunun iyi olup olmadığını işaretle. Bu puanlar ileride soru kalitesini ölçmek ve makine öğrenmesi modeli eğitmek için veri seti olacak.
- **Kapsam:** `appsettings.json` → `Quiz:ExcludeCategories` (varsayılan: Projeler, firmaya özel notlar).
- **Önizleme:** `dotnet run -- --quiz-onizleme 7` kaydetmeden 7 günlük örnek quiz yazar.

## Yedekleme ve iki bilgisayar

OneNote'tan aktarım bitince notlar **doğrudan bu siteye** yazılacak; veritabanı notların tek kopyası olur. Site iki bilgisayarda kullanılıyor ama **asla aynı anda açık olmuyor**. Buna göre:

| Konu | Seçim | Neden |
|---|---|---|
| Veritabanı | **SQLite**, her bilgisayarda yerel tek dosya | Kurulum yok, internetsiz çalışır. Yedek = dosyanın kopyası, geri yükleme = dosyayı değiştirmek. Kişisel not uygulaması için SQL Server gereğinden ağırdı (her bilgisayara kurulum, servis izinleri). Bulut veritabanı (Azure SQL) ücretsiz katmanda bile abonelik ve fatura riski getirir; iki bilgisayar aynı anda açık olmadığı için gerek yok. |
| Bulut | **OneDrive** | Windows'ta yerleşik, ek kurulum / API anahtarı yok. 5 GB ücretsiz alan 14 yedek için fazlasıyla yeter. Silinen dosya için geri dönüşüm kutusu ve sürüm geçmişi var. |

### Nasıl çalışır

- **Yedek ne zaman alınır:** her gün ilk açılışta, not kaydettikten 1 dk sonra (art arda kayıtlar tek yedek olur) ve site kapanırken (yedeklenmemiş değişiklik varsa).
- **Nereye:** `Backups/ABAPKnowledgeBase-yyyyMMdd.db` ve OneDrive'da `ABAPKnowledgeBase-Yedekler/`. Günün dosyası gün içinde güncellenir; yerelde ve OneDrive'da **son 14 gün** tutulur. Her yedek kendi başına açılabilen tam bir SQLite veritabanıdır.
- **Diğer bilgisayara geçince:** site açılırken OneDrive'daki `son-yedek.json` okunur. Diğer bilgisayar daha yeni yedek bıraktıysa önce o yedek buraya yüklenir, sonra site açılır. Kendiliğinden olur.
- **İki tarafta da değişiklik varsa** (ör. bir bilgisayarda yedek OneDrive'a çıkmadan diğerinde not yazıldı): site hiçbir şeyi ezmeden açılmaz ve hangisinin kalacağını sorar:

  ```powershell
  dotnet run -- --restore-from-cloud   # diğer bilgisayarınki kalsın (buradakinin kopyası Backups\oncesi-... olarak saklanır)
  dotnet run -- --keep-local           # bu bilgisayarınki kalsın (OneDrive'a yazılır)
  ```

- **OneDrive senkronu bitmemişse** site açılmaz, "henüz tam inmemiş" der. Birkaç dakika sonra tekrar aç.
- **Boş veritabanı OneDrive'a hiç gönderilmez.** Yeni kurulmuş, henüz notu olmayan bir bilgisayar diğerinin yedeğini ezemez.
- Yedek hatası siteyi durdurmaz, konsola yazılır.

### Kurallar

1. **Siteyi Ctrl+C ile kapat.** Kapanış yedeği bu sırada alınır ("pencereyi kapatma" yazısı görünür). Pencereyi X ile kapatırsan son 1 dakikadaki değişiklik bir sonraki açılışa kalabilir.
2. **Geçmeden önce OneDrive'ın senkronladığından emin ol** (görev çubuğundaki OneDrive simgesi "güncel").
3. Siteyi iki bilgisayarda aynı anda açma.

### SQL Server'dan SQLite'a geçiş (bir kez, notların olduğu bilgisayarda)

1. `git pull`
2. `dotnet run`. SQLite boş olduğu ve bu bilgisayarda eski `.\SQLEXPRESS` veritabanı bulunduğu için notlar, firmalar, resimler, etiketler ve kaynaklar kendiliğinden SQLite'a kopyalanır. Eski veritabanında etiket/kaynak ister metin sütunlarında ister ayrı tablolarda olsun aktarılır. Konsolda her tablo için `SQL Server: N  SQLite: N ✓` satırları (etiketli ve kaynaklı not sayıları dahil) ve hemen ardından `Bulut kopyası çıkarıldı` görünür.
3. Diğer bilgisayarda: OneDrive senkronlanınca `git pull` + `dotnet run` yap, notlar kendiliğinden gelir.

SQL Server'daki veriye dokunulmaz (sadece okunur); bir şey ters giderse eski hâl orada durur. Aktarım elle de çalıştırılabilir: `dotnet run -- --import-sqlserver`. SQLite'ta not varsa ikinci kez aktarmaz. Kaynak bağlantı: `appsettings.json` → `ConnectionStrings:SqlServer`. Geçiş oturduktan sonra SQL Server kaldırılabilir.

### Elle komutlar

```powershell
dotnet run -- --backup                     # hemen yedek al, OneDrive'a kopyala ve çık
dotnet run -- --quiz-onizleme 7            # kaydetmeden 7 günlük örnek quiz yaz
dotnet run -- --restore-from-cloud         # OneDrive'daki son yedeği yükle
dotnet run -- --restore "C:\yol\ABAPKnowledgeBase-20261113.db"  # belirli bir günün yedeğini yükle
```

Bir yedeği yüklemeden önce mevcut veritabanının kopyası her zaman `Backups/oncesi-...db` olarak alınır.

```json
"Backup": { "Enabled": true, "KeepLastN": 14, "LocalFolder": "Backups", "CloudFolder": "", "CloudFolderName": "ABAPKnowledgeBase-Yedekler" }
```

`CloudFolder` boşsa OneDrive klasörü otomatik bulunur. Google Drive / Dropbox kullanacaksan senkron klasörün tam yolunu buraya yaz.

### OneNote'tan geçiş

OneNote'tan son aktarımı yaptıktan sonra `appsettings.json` → `"OneNote": { "ImportEnabled": false }` yap. Bundan sonra `--import-onenote` hiçbir şey yapmaz.

Açık olsa bile tam aktarım (`--import-onenote --apply`, veritabanını silip OneNote'tan yeniden kurar), sitede yazılmış (OneNote'tan gelmeyen) not varsa durur; `--force` olmadan silmez. Eksikleri eklemek için `--add --apply` kullanılabilir. Veritabanına yazan her aktarımdan önce tam yedek alınır.

## Veri modeli

| Tablo | İçerik |
|---|---|
| Notes | Başlık, metin, kod, kategori, firma, kaynak + sayfa no, sıra, tarihler |
| Categories | 10 ABAP kategorisi + Projeler |
| Companies | Projeler kategorisindeki firmalar |
| Tags / NoteTags | Etiketler ve not–etiket bağlantıları (çoka çok) |
| Sources | Notların kaynağı: OneNote defter + bölüm veya dosya adı |
| NoteImages | Resimler ve resimlerden okunan yazı (OCR) |
| QuizDays / QuizQuestions | Günlük quiz'ler, sorular, verilen cevaplar ve 👍/👎 puanları |
| NoteReviews | Her notun aralıklı tekrar kutusu ve sonraki tekrar tarihi |

## Bilinçli sınırlamalar

- Bu bir **yerel prototip**: **kullanıcı girişi yok, internete açma**.
- Kategori seçimi otomatik kaydedilmez; önerinin butonuna basarak seçersin.
- Arama her notta "içinde geçiyor mu" kontrolüyle çalışır (büyük/küçük harf ve İ/ı farkı gözetmez), en fazla 1000 aday not inceler. Not sayısı on binlere çıkarsa SQLite FTS5 tam metin aramasına geçilmeli.
- Veritabanı şeması EF Core migration'larıyla (`Data/Migrations`) yönetilir ve uygulama açılırken otomatik güncellenir. Modele alan eklersen: `dotnet ef migrations add <Ad> --configuration Release --output-dir Data/Migrations` (araç `dotnet tool restore` ile kurulur; uygulama açıkken `bin/Debug` kilitli olduğu için Release kullanılır).

## Örnek test

Başlık: `?= downcasting`, içerik: `Referans tipi dönüşümü`, kod: `lo_struc ?= cl_abap_typedescr=>describe_by_data( ls_data ).` → **Kategori öner** → Nesne Yönelimli ABAP.

Sitede `?=` veya `CL_GUI_ALV_GRID` araması yaparak test edebilirsin.
