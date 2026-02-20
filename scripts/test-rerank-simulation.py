import json
import urllib.request
import re

# 1. Get embedding
query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
vector = json.loads(urllib.request.urlopen(req).read())["embeddings"][0]

# 2. Search top 30 (overFetchK)
search_req = json.dumps({"vector": vector, "limit": 30, "with_payload": True}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
results = json.loads(urllib.request.urlopen(req2).read())["result"]

# 3. Extract keywords (simulating C# ExtractKeywords)
cleaned = re.sub(r'[은는이가을를의에서로부터까지와과도만요?!.,\s]+', ' ', query)
keywords = [w for w in cleaned.split() if len(w) >= 2]
print(f"Query: {query}")
print(f"Keywords: {keywords}")
print(f"Candidates: {len(results)}\n")

# 4. Re-rank with keyword boost (0.10 per keyword)
boost_per_kw = 0.10
ranked = []
for r in results:
    text = r["payload"].get("text", "")
    doc = r["payload"].get("document_id", "?")
    idx = r["payload"].get("chunk_index", "?")
    orig_score = r["score"]

    match_count = sum(1 for kw in keywords if kw in text)
    matched_kws = [kw for kw in keywords if kw in text]
    boosted = orig_score + match_count * boost_per_kw
    has_500 = "500" in text and "시간" in text

    ranked.append({
        "doc": doc, "idx": idx, "orig": orig_score, "boosted": boosted,
        "matches": matched_kws, "has_500": has_500,
        "header": text.split("\n")[0][:80]
    })

ranked.sort(key=lambda x: x["boosted"], reverse=True)

print("=== Re-ranked results (top 10) ===\n")
for i, r in enumerate(ranked[:10]):
    marker = " *** 500시간 ***" if r["has_500"] else ""
    topk = " <-- TOP 3" if i < 3 else ""
    print(f"  #{i+1:2d} boosted={r['boosted']:.4f} (orig={r['orig']:.4f}, +{len(r['matches'])}kw) {r['doc']}:chunk{r['idx']}{marker}{topk}")
    print(f"      keywords: {r['matches']}")
    print(f"      {r['header']}")
    print()
