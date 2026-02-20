import json
import urllib.request

# Get embedding
query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
vector = json.loads(urllib.request.urlopen(req).read())["embeddings"][0]

# Search with text keyword filter
print("=== Filtered search: text must contain both keywords ===\n")
search_req = json.dumps({
    "vector": vector,
    "limit": 5,
    "with_payload": True,
    "filter": {
        "must": [
            {"key": "text", "match": {"text": "패드"}},
            {"key": "text", "match": {"text": "교체"}}
        ]
    }
}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
results = json.loads(urllib.request.urlopen(req2).read())["result"]

for i, r in enumerate(results):
    score = r["score"]
    doc = r["payload"].get("document_id", "?")
    idx = r["payload"].get("chunk_index", "?")
    text = r["payload"].get("text", "")
    has_500 = "500" in text and "시간" in text
    first_line = text.split("\n")[0][:120]
    print(f"  #{i+1} score={score:.4f} {doc}:chunk{idx} {'[500시간]' if has_500 else ''}")
    print(f"     {first_line}")
    print()
