import time
from elasticsearch import Elasticsearch, helpers
from prepare_data import iter_docs

ES_URL = "http://localhost:9200"
INDEX = "filmler"
LIMIT = None
CHUNK = 2000    # her _bluk isteğinde kaç belge gideceği

def actions(limit):                         # iter_docs'tan gelen belgeleri _bulk formatına çevirir
    for doc in iter_docs(limit=limit): 
        yield{
            "_index" : INDEX,
            "_id" : doc["tconst"],        # kimliği biz veriyoruz: tekrar yüklemede kopya olmaz
            "_source": doc,               # belgenin kendisi
        }

def main():
    es = Elasticsearch(ES_URL, request_timeout=60)
    print("bağlantı: ", es.info()["version"]["number"])

    start = time.perf_counter()   # süreyi ölçmeye başla

    ok, errors = helpers.bulk(
        es,
        actions(LIMIT),
        chunk_size = CHUNK,
        raise_on_error = False,    # ilk hatada durma hepsini dene 
)

    sure = time.perf_counter() - start

    print(f"yüklenen: {ok}  hatalı: {len(errors)}  süre: {sure:.1f} sn")
    if errors:
        print("ilk hata: ", errors[0])

if __name__ == "__main__":
    main()
