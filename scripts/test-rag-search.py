import json
import urllib.request

# 1. Get embedding from Ollama
print("=== Step 1: Get embedding for query ===")
query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
resp = urllib.request.urlopen(req)
embed_data = json.loads(resp.read())
vector = embed_data["embeddings"][0]
print(f"Embedding dim: {len(vector)}")

# 2. Search Qdrant with the embedding
print("\n=== Step 2: Search Qdrant (top 5, no min score filter) ===")
search_req = json.dumps({
    "vector": vector,
    "limit": 5,
    "with_payload": True,
    "score_threshold": 0.0
}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
resp2 = urllib.request.urlopen(req2)
search_data = json.loads(resp2.read())

results = search_data["result"]
print(f"Results: {len(results)}\n")

for i, r in enumerate(results):
    score = r["score"]
    payload = r["payload"]
    doc = payload.get("document_id", "?")
    idx = payload.get("chunk_index", "?")
    text = payload.get("text", "")[:300]

    passed = "PASS" if score >= 0.55 else "FILTERED (< 0.55)"
    print(f"--- Result {i+1}: score={score:.4f} [{passed}] ---")
    print(f"Doc: {doc}, Chunk: {idx}")
    print(f"Text: {text}")
    print()

# 3. Check with MinScore 0.55
above_threshold = [r for r in results if r["score"] >= 0.55]
print(f"\n=== Summary ===")
print(f"Total results: {len(results)}")
print(f"Above MinScore 0.55: {len(above_threshold)}")
print(f"Top-3 (actual RAG results):")
for i, r in enumerate(above_threshold[:3]):
    score = r["score"]
    doc = r["payload"].get("document_id", "?")
    has_500 = "500" in r["payload"].get("text", "")
    print(f"  {i+1}. {doc} (score={score:.4f}) {'[contains 500]' if has_500 else ''}")
