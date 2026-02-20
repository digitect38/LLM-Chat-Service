import json
import urllib.request

# Get embedding
query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
resp = urllib.request.urlopen(req)
vector = json.loads(resp.read())["embeddings"][0]

# Search top 20
search_req = json.dumps({"vector": vector, "limit": 20, "with_payload": True}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
results = json.loads(urllib.request.urlopen(req2).read())["result"]

print(f"Query: {query}")
print(f"Results: {len(results)}\n")

for i, r in enumerate(results):
    score = r["score"]
    doc = r["payload"].get("document_id", "?")
    idx = r["payload"].get("chunk_index", "?")
    text = r["payload"].get("text", "")
    has_500 = "500" in text and ("시간" in text or "pad" in text.lower())
    has_200 = "200시간" in text
    topk_mark = " <-- TopK=3" if i < 3 else (" <-- TopK=5" if i < 5 else "")

    marker = ""
    if has_500: marker += " [500시간 PAD]"
    if has_200: marker += " [200시간 DISK]"

    # Show first line of text (the header)
    first_line = text.split("\n")[0][:100]
    print(f"  #{i+1:2d} score={score:.4f} {doc}:chunk{idx}{marker}{topk_mark}")
    print(f"      {first_line}")
