import csv
import json
import gzip

tBasics_path = "data/title.basics.tsv.gz"
skip_types = {"tvEpisode","videoGame"}
LIMIT = 50000

tRatings_path = "data/title.ratings.tsv.gz"

def clean(value):                             # boş değerleri None yapıyoruz
    if value == r"\N" or value == "":         #  \ özel karakter başlatır, baştaki r "bunu olduğu gibi al" demek
        return None  
    return value

def to_int(value):                        # sayılar integer'a çeviriyoruz
    value = clean(value)
    if value is None:
        return None
    try:
        return int(value)
    except ValueError:
        return None                       # beklenmedik değer gelrse None döner
    
def parse_genres(value):                    # virgülle ayrılmış metni listeye çevirir
    value = clean(value)
    if value is None:
        return []
    return value.split(",")

def load_ratings():    # tconst -> (puan, oy sayısı) sözlüğü oluşturur
    ratings = {}
    with gzip.open(tRatings_path, "rt", encoding="utf-8") as f:
        reader = csv.reader(f, delimiter="\t", quoting=csv.QUOTE_NONE)
        next(reader) # başlık satırı 
        for row in reader:
            if len(row) < 3:
                continue
            tconst = row[0]
            rating = float(row[1])
            votes = int(row[2])
            ratings[tconst] = (rating, votes)    # sözlüğe bir kayıt ekliyor. anahtar -film kimliği, değer -iki elemanlı bir tuple
    return ratings 


def iter_docs(limit=None, stats=None):
    """Dosyayı okur, temizler ve belgeleri tek tek üretir (generator)."""
    # stats: sayaçları dışarıya taşımak için; verilmezse sadece içeride tutulur
    if stats is None:
        stats = {}
    stats["read"] = 0
    stats["kept"] = 0
    stats["skipped"] = 0
    stats["with_rating"] = 0

    ratings = load_ratings()     # puanları belleğe alıyoruz 
    print(f"puan kaydı : {len(ratings)}")     # kaç tane geldi? 

    with gzip.open(tBasics_path,"rt", encoding="utf-8" ) as f:               # with bloğu bitince dosyayı otomatik kapatır
        reader = csv.reader(f, delimiter="\t", quoting=csv.QUOTE_NONE)         # csv.reader, her satırı Tab'lardan bölüp liste olarak verir
        next(reader)     # başlık satırını (ilk satırları) okur atlar           # quoting=csv.QUOTE_NONE olmazsa tırnak içeren film adlarında satırı yanlış böler

        for row in reader:   # dosyayı satır satır okur, tamamını belleğe almaz
            stats["read"] += 1

            if limit is not None and stats["read"] > limit:   # limit verilmişse orada dur
                break

            if len(row) < 9:    # bozuk satır varsa atla
                stats["skipped"] += 1
                continue       # bu satırı bırak, sonraki adıma geç 

            # sütunları isimlendiriyoruz
            tconst = row[0]
            title_type = row[1]
            primary_title = row[2]
            original_title = row[3]
            is_adult = row[4]
            start_year = row[5]
            end_year = row[6]
            runtime = row[7]
            genres = row[8]

            if title_type in skip_types or is_adult == "1":
                stats["skipped"] += 1
                continue

            rating_info = ratings.get(tconst)  # yokse None döner

            if rating_info is not None:
                stats["with_rating"] += 1

            stats["kept"] += 1

            # elasticsearch'e gönderilecek belge, anahtarlar mapping'deki alan adlarıyla birebir aynı olmalı 
            # dynamic: strict yüzünden bir harf farkı bile hata verir
            yield {                                # yield: belgeyi ver, fonksiyonu dondur, sonraki istendiğinde devam et
                "tconst": tconst,
                "titleType": title_type,
                "primaryTitle": clean(primary_title),
                "originalTitle": clean(original_title),
                "startYear": to_int(start_year),
                "endYear": to_int(end_year),
                "runtimeMinutes": to_int(runtime),
                "genres": parse_genres(genres),
                "rating": rating_info[0] if rating_info is not None else None,
                "votes":  rating_info[1] if rating_info is not None else None,    # Eğer sadece rating_info[0] yazsaydık ve film puansız olsaydı, None[0] demiş olurduk. 
            }                                                                     # Python buna TypeError: 'NoneType' object is not subscriptable der ve program çöker


def main():
    stats = {}
    examples = []

    for doc in iter_docs(limit=limit, stats=stats):     # generator'ı tüketiyoruz
        if doc["rating"] is not None and len(examples) < 5:
            examples.append(doc)

    for doc in examples:
        print(json.dumps(doc, ensure_ascii=False, indent = 2))

    print(f"okunan: {stats['read']}   tutulan: {stats['kept']}   atlanan:  {stats['skipped']}  puanlı: {stats['with_rating']}")


if __name__ == "__main__":    # bu dosya doğrudan çalıştırıldıysa main()'i çağır
    main()