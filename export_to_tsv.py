import time
from prepare_data import iter_docs

OUT_PATH = "data/filmler_sql_u16.tsv"


def temizle(deger):
    """None -> boş, Tab ve satır sonlarını boşluğa çevir."""
    if deger is None:
        return ""
    metin = str(deger)
    return metin.replace("\t", " ").replace("\n", " ").replace("\r", " ")


def main():
    start = time.perf_counter()
    count = 0

    with open(OUT_PATH, "w", encoding="utf-16", newline="") as f:
        for doc in iter_docs():
            satir = "\t".join([
                temizle(doc["tconst"]),
                temizle(doc["titleType"]),
                temizle(doc["primaryTitle"]),
                temizle(doc["originalTitle"]),
                temizle(doc["startYear"]),
                temizle(doc["endYear"]),
                temizle(doc["runtimeMinutes"]),
                temizle(",".join(doc["genres"])),
                temizle(doc["rating"]),
                temizle(doc["votes"]),
            ])
            f.write(satir + "\n")
            count += 1

    sure = time.perf_counter() - start
    print(f"yazılan satır: {count}   süre: {sure:.1f} sn")


if __name__ == "__main__":
    main()