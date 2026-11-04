# ABAP Bilgi Bankası — Kişisel Not Uygulaması

**Amaç:** Notu yaz → kategori önerisi al → elle onaylayıp kaydet → başlık/açıklama/kod/etikette ara.

ASP.NET Core MVC (.NET 10) + Entity Framework Core + SQL Server.

## Kurulum (Windows)

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) yükle.
2. SQL Server Express kur. Varsayılan bağlantı `appsettings.json` içinde:

   ```json
   "Default": "Server=.\\SQLEXPRESS;Database=ABAPKnowledgeBase;Trusted_Connection=True;TrustServerCertificate=True"
   ```

   LocalDB kullanıyorsan `Server=(localdb)\\MSSQLLocalDB` yap; başka bir SQL Server'ın varsa `Server=` kısmını ona göre değiştir.
3. Terminalde bu klasöre git ve çalıştır:

   ```powershell
   dotnet restore
   dotnet run --urls "http://localhost:5168"
   ```

4. Tarayıcıda **http://localhost:5168** adresine gir. Uygulama ilk çalıştırmada veritabanını ve 11 kategoriyi (10 ABAP konusu + Projeler) oluşturur.

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

Var olan notlar, sıralamalar ve firmalar değişmez; yeni sayfalar kategorilerinin sonuna eklenir. Projeler kategorisine giden sayfalar bu modda eklenmez. Birebir aynı başlık ve içeriğe sahip sayfalar ikinci kez eklenmez. OneNote'ta sonradan değişen sayfaların üzerine yazılmaz, önizlemede listelenir (içi boş olan notlar doldurulur).

Eşlemede `"__ATLA__"` değeri o sayfa, bölüm veya bölüm grubunu (`"defter/grup/*"`) tamamen dışarıda bırakır.

Not gövdesinde ```` ``` ```` satırları arası kod bloğu, tek başına `[resim:N]` satırı notun N. resmi olarak gösterilir.

## PDF notlarını içe aktarma (eski yöntem)

PDF'lerden çıkarılmış notlar `ImportData/notlar.json` dosyasına konursa (alanlar: `Title`, `Body`, `Category`, `SourceFile`, `SourcePage`, `Tags`) şu komutla yüklenir:

```powershell
dotnet run -- --import-notes
```

- Komut notları ekler, sayıyı yazar ve kapanır (web sunucusu açılmaz).
- Aynı kaynak dosya + sayfa numarasına sahip not zaten varsa tekrar eklenmez; komutu birden çok kez çalıştırmak güvenlidir.
- JSON'daki kategori adı veritabanında yoksa not atlanır ve konsola yazılır.

## Özellikler

- Not ekleme, düzenleme, silme (onaylı), listeleme ve detay görüntüleme
- Üç kolonlu okuma ekranı (kategoriler, sayfalar, not) ve sağ panelde düzenleme
- Projeler kategorisinde firmalara göre gruplama
- Sayfaları sürükle-bırak ile sıralama
- OneNote'tan resimleriyle aktarım; resimlerdeki yazıda (OCR) arama
- Açık / koyu tema
- 10 ABAP kategorisi + Projeler; ABAP terimlerine dayalı kategori önerisi (tam kelime eşleşmesi, ağırlıklı puan, kullanıcı onaylı)
- Teknik sembolleri de (`?=`, `VALUE #(`) arayan literal arama
- Kaynak dosya adı ve sayfa numarası kaydı
- 404 / hata durumunda uygulamanın kendi hata sayfası

## Bilinçli sınırlamalar

- Bu bir **yerel prototip**: **kullanıcı girişi yok, internete açma**.
- Kategori seçimi otomatik kaydedilmez; önerinin butonuna basarak seçersin.
- Arama SQL `LIKE` ile çalışır ve tek seferde en fazla 200 not gösterir. Not sayısı çok büyürse SQL Server Full-Text Search'e geçilmeli.
- Veritabanı şeması EF Core migration'larıyla (`Data/Migrations`) yönetilir ve uygulama açılırken otomatik güncellenir. Modele alan eklersen: `dotnet ef migrations add <Ad> --configuration Release --output-dir Data/Migrations` (araç `dotnet tool restore` ile kurulur; uygulama açıkken `bin/Debug` kilitli olduğu için Release kullanılır).
- PDF'ten çıkarılan notlarda metin düzeni bozuk olabilir (sütunlar karışmış, kod `Code` yerine `Body` içinde).

## Örnek test

Başlık: `?= downcasting`, içerik: `Referans tipi dönüşümü`, kod: `lo_struc ?= cl_abap_typedescr=>describe_by_data( ls_data ).` → **Kategori öner** → Nesne Yönelimli ABAP.

Sitede `?=` veya `CL_GUI_ALV_GRID` araması yaparak test edebilirsin.
