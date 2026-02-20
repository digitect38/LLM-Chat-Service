import json
import urllib.request
import re

query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
vector = json.loads(urllib.request.urlopen(req).read())["embeddings"][0]

# Search top 50
search_req = json.dumps({"vector": vector, "limit": 50, "with_payload": True}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
results = json.loads(urllib.request.urlopen(req2).read())["result"]

# Extract keywords
cleaned = re.sub(r'[은는이가을를의에서로부터까지와과도만요?!.,\s]+', ' ', query)
keywords = [w for w in cleaned.split() if len(w) >= 2]
print(f"Keywords: {keywords}, Candidates: {len(results)}\n")

# Find all 500시간 chunks in the candidates
print("=== 500시간 chunks in candidates ===")
for i, r in enumerate(results):
    text = r["payload"].get("text", "")
    if "500" in text and "시간" in text:
        doc = r["payload"].get("document_id", "?")
        idx = r["payload"].get("chunk_index", "?")
        match_count = sum(1 for kw in keywords if kw in text)
        matched = [kw for kw in keywords if kw in text]
        boosted = r["score"] + match_count * 0.10
        print(f"  orig_rank=#{i+1} score={r['score']:.4f} boosted={boosted:.4f} {doc}:chunk{idx}")
        print(f"  keywords: {matched}")
        first_line = text.split("\n")[0][:100]
        print(f"  {first_line}\n")

# Re-rank
boost = 0.10
ranked = []
for r in results:
    text = r["payload"].get("text", "")
    doc = r["payload"].get("document_id", "?")
    idx = r["payload"].get("chunk_index", "?")
    match_count = sum(1 for kw in keywords if kw in text)
    boosted = r["score"] + match_count * boost
    has_500 = "500" in text and "시간" in text
    ranked.append({"doc": doc, "idx": idx, "orig": r["score"], "boosted": boosted,
                   "has_500": has_500, "header": text.split("\n")[0][:90]})

ranked.sort(key=lambda x: x["boosted"], reverse=True)
print("\n=== Re-ranked TOP 5 ===")
for i, r in enumerate(ranked[:5]):
    m = " *** 500시간 ***" if r["has_500"] else ""
    print(f"  #{i+1} boosted={r['boosted']:.4f} (orig={r['orig']:.4f}) {r['doc']}:chunk{r['idx']}{m}")
    print(f"     {r['header']}")
