"""Quiz aşama 2: TF-IDF ile her notun anahtar terimlerini bulur ve veritabanına yazar.

TF-IDF bir kelimenin bir not için ne kadar "özel" olduğunu ölçer:
  - TF (term frequency): kelime bu notta ne kadar sık geçiyor?
  - IDF (inverse document frequency): kaç notta geçiyor? Az notta geçen kelime daha ayırt edicidir.
  Skor = TF x IDF. "ve", "için" gibi her yerde geçen kelimeler düşük, "cl_salv_table" gibi
  sadece birkaç notta geçenler yüksek puan alır.

Sonuç NoteKeyTerms tablosuna yazılır (tabloyu C# uygulaması oluşturur). Quiz, boşluk doldurma
sorularında rastgele bir kelime yerine notun en anlamlı terimini gizler.

Kullanım:
  ml\\.venv\\Scripts\\python.exe ml\\anahtar_terimler.py                 # varsayılan veritabanına yazar
  ml\\.venv\\Scripts\\python.exe ml\\anahtar_terimler.py --onizle 5      # yazmadan 5 notun terimlerini gösterir
  ml\\.venv\\Scripts\\python.exe ml\\anahtar_terimler.py --db C:\\yol\\abap-notlar.db
"""
import argparse
import os
import re
import sqlite3
from collections import Counter
from datetime import datetime, timezone

import pandas as pd
from sklearn.feature_extraction.text import TfidfVectorizer

VARSAYILAN_DB = os.path.expandvars(r"%LOCALAPPDATA%\ABAPKnowledgeBase\abap-notlar.db")
TERIM_SAYISI = 10  # her not için saklanacak en iyi terim sayısı

# Türkçe bağlaçlar, zamirler, yardımcı fiiller: anlam taşımaz, terim olamaz.
DURAK = set("""
acaba ama ancak artık aslında az bana bazen bazı belki ben beni benim bile bir biraz birçok birden biri birkaç birşey
biz bize bizi bizim bu buna bunda bundan bunlar bunları bunların bunu bunun burada böyle böylece da daha de defa değil
diğer diye dolayı en fakat gibi göre hala hangi hatta hem henüz hep hepsi her herhangi herkes hiç hiçbir için ile ise
işte kadar kendi kendine ki kim kimse mi mu mü mı nasıl ne neden nerede nereye niye o olan olarak oldu olduğu olduğunu
olmak olması olsun olur on ona ondan onlar onları onu onun orada öyle sadece sanki sen siz şey şimdi şu şuna şunu tüm
var ve veya ya yani yine yok zaten çok çünkü önce sonra üzere yada eğer ayrıca bunun şöyle olabilir olacak olan olup
yapılır yapmak yapar yapılan kullanılır kullanılan kullanarak kullanılabilir gerekir gerek lazım sonra önce şekilde
şekli halinde değer değeri olursa olmaz yerine içinde içine arasında üzerinde altında birlikte tarafından aynı başka
yeni eski ilk son tek iki üç dört beş not örnek örneğin mesela bakınız ekle ekran https http www com
""".split())

# ABAP'ın neredeyse her kodda geçen anahtar kelimeleri: notu ayırt etmez.
ABAP_GENEL = set("""
data type types table tables of to from into where and or not is in at end if endif loop endloop select endselect
value line lines begin method endmethod form endform perform using changing exporting importing returning
ref like initial abap_true abap_false sy-subrc write ls lt lv gt gs
""".split())

# Kelime: harf/rakam/_ ile başlar, içinde _ / ~ - olabilir (cl_salv_table, sy-subrc, /ui2/...).
KELIME = re.compile(r"[\w/][\w/~\-]*[\w]|\w")
RESIM_SATIRI = re.compile(r"^\s*\[resim:\d+\]\s*$", re.MULTILINE)
KESME = re.compile(r"['’‘]")  # "ALV'de" -> "ALV" + "de"
IFADE_SINIRI = re.compile(r"[\n.,:;!?()\[\]{}|\"“”→>=]+")


def kucuk(s: str) -> str:
    """Türkçe uyumlu küçük harf: I -> ı, İ -> i."""
    return s.replace("I", "ı").replace("İ", "i").lower()


def kelimeler(metin: str) -> list[str]:
    """Metni kelimelere böler; Türkçe kesme işaretinden sonraki eki atar."""
    sonuc = []
    for parca in KESME.split(metin):
        sonuc.extend(KELIME.findall(parca))
    return sonuc


def gecerli(k: str) -> bool:
    kk = kucuk(k)
    return (len(k) >= 3 and not k.isdigit() and any(c.isalpha() for c in k)
            and kk not in DURAK and kk not in ABAP_GENEL)


def terimler(metin: str) -> list[str]:
    """TfidfVectorizer'ın analyzer'ı: tek kelimeler + aynı ifade içinde art arda iki anlamlı kelime
    ("field catalog"). İkililer satır sonu ve noktalamayı aşmaz ("Constructor, Static" ikili değildir)."""
    sonuc = []
    for ifade in IFADE_SINIRI.split(metin):
        kel = kelimeler(ifade)
        sonuc += [kucuk(k) for k in kel if gecerli(k)]
        sonuc += [kucuk(a) + " " + kucuk(b) for a, b in zip(kel, kel[1:])
                  if gecerli(a) and gecerli(b) and a.isalpha() and b.isalpha()]
    return sonuc


def gorunum(metin: str, terim: str) -> str | None:
    """Terimin notta en sık geçen yazılışı (ör. "salv" -> "SALV"). Metinde aynen geçmiyorsa None."""
    parcalar = terim.split(" ")
    sayac = Counter()
    for ifade in IFADE_SINIRI.split(metin):
        kel = kelimeler(ifade)
        for i in range(len(kel) - len(parcalar) + 1):
            aday = kel[i:i + len(parcalar)]
            if [kucuk(a) for a in aday] == parcalar:
                sayac[" ".join(aday)] += 1
    return sayac.most_common(1)[0][0] if sayac else None


def notlari_oku(db: sqlite3.Connection) -> pd.DataFrame:
    df = pd.read_sql_query("SELECT Id, Title, Body, Code FROM Notes", db)
    df = df.fillna("")
    # Başlık iki kez eklenir: başlıktaki kelimeler notun konusunu en iyi anlatır.
    df["Metin"] = (df["Title"] + "\n" + df["Title"] + "\n"
                   + df["Body"].str.replace(RESIM_SATIRI, "", regex=True).str.replace("```", "", regex=False)
                   + "\n" + df["Code"])
    return df


def hesapla(df: pd.DataFrame) -> pd.DataFrame:
    vektor = TfidfVectorizer(
        analyzer=terimler,
        sublinear_tf=True,  # 10 kez geçen kelime 10 kat değil, log ölçeğinde daha önemli sayılır
        max_df=0.25,        # notların %25'inden fazlasında geçen kelime ayırt edici değildir
    )
    matris = vektor.fit_transform(df["Metin"])
    sozluk = vektor.get_feature_names_out()

    satirlar = []
    for i, not_id in enumerate(df["Id"]):
        satir = matris.getrow(i)
        en_iyiler = sorted(zip(satir.indices, satir.data), key=lambda x: -x[1])
        sira = 0
        for sutun, skor in en_iyiler:
            yazilis = gorunum(df["Metin"].iat[i], sozluk[sutun])
            if yazilis is None or len(yazilis) > 120:
                continue
            sira += 1
            satirlar.append({"NoteId": int(not_id), "Rank": sira, "Term": yazilis, "Score": round(float(skor), 4)})
            if sira == TERIM_SAYISI:
                break
    return pd.DataFrame(satirlar, columns=["NoteId", "Rank", "Term", "Score"])


def yaz(db: sqlite3.Connection, terim_df: pd.DataFrame) -> None:
    simdi = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
    with db:  # tek işlem: yarıda kalırsa eski terimler korunur
        db.execute("DELETE FROM NoteKeyTerms")
        db.executemany(
            "INSERT INTO NoteKeyTerms (NoteId, Rank, Term, Score, ComputedAt) VALUES (?, ?, ?, ?, ?)",
            [(r.NoteId, r.Rank, r.Term, r.Score, simdi) for r in terim_df.itertuples()])


def main() -> None:
    ap = argparse.ArgumentParser(description="TF-IDF ile notların anahtar terimlerini hesaplar.")
    ap.add_argument("--db", default=VARSAYILAN_DB, help="SQLite veritabanı yolu")
    ap.add_argument("--onizle", type=int, metavar="N", help="yazmadan rastgele N notun terimlerini göster")
    args = ap.parse_args()

    db = sqlite3.connect(args.db, timeout=30)
    try:
        df = notlari_oku(db)
        terim_df = hesapla(df)
        if args.onizle:
            for _, n in df.sample(min(args.onizle, len(df))).iterrows():
                t = terim_df[terim_df["NoteId"] == n["Id"]]
                print(f"• {n['Title']}: " + ", ".join(f"{r.Term} ({r.Score:.2f})" for r in t.itertuples()))
            return
        yaz(db, terim_df)
        print(f"{len(df)} not, {len(terim_df)} anahtar terim yazıldı.")
    finally:
        db.close()


if __name__ == "__main__":
    main()
