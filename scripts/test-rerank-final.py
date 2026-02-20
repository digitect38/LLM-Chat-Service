import json
import urllib.request
import re

query = "패드 교체 시기는 언제인가요?"
embed_req = json.dumps({"model": "nomic-embed-text", "input": query}).encode()
req = urllib.request.Request("http://localhost:11434/api/embed", data=embed_req,
                            headers={"Content-Type": "application/json"})
vector = json.loads(urllib.request.urlopen(req).read())["embeddings"][0]

# Fetch all 100 candidates
search_req = json.dumps({"vector": vector, "limit": 100, "with_payload": True}).encode()
req2 = urllib.request.Request("http://localhost:6333/collections/knowledge/points/search",
                              data=search_req, headers={"Content-Type": "application/json"})
results = json.loads(urllib.request.urlopen(req2).read())["result"]

# Simulate ExtractKeywords + ExpandKeywords
cleaned = re.sub(r'[은는이가을를의에서로부터까지와과도만요?!.,\s]+', ' ', query)
keywords = [w for w in cleaned.split() if len(w) >= 2]

expansion_map = {
    "시기": ["기준", "시간", "수명", "주기"],
    "언제": ["기준", "시간", "시점"],
}
expanded = set(keywords)
for kw in keywords:
    if kw in expansion_map:
        for r in expansion_map[kw]:
            expanded.add(r)
expanded = list(expanded)

print(f"Keywords: {keywords}")
print(f"Expanded: {expanded}")
print(f"Candidates (before MinScore): {len(results)}")

# Pre-filter by MinScore 0.45
min_score = 0.45
candidates = [r for r in results if r["score"] >= min_score]
print(f"Candidates (after MinScore {min_score}): {len(candidates)}\n")

# Re-rank
boost = 0.10
ranked = []
for r in candidates:
    text = r["payload"].get("text", "")
    doc = r["payload"].get("document_id", "?")
    idx = r["payload"].get("chunk_index", "?")
    match_count = sum(1 for kw in expanded if kw in text)
    matched = [kw for kw in expanded if kw in text]
    boosted = r["score"] + match_count * boost
    has_500 = "500" in text and "시간" in text
    ranked.append({"doc": doc, "idx": idx, "orig": r["score"], "boosted": boosted,
                   "matches": matched, "has_500": has_500,
                   "header": text.split("\n")[0][:90]})

ranked.sort(key=lambda x: x["boosted"], reverse=True)

print("=== Final re-ranked TOP 5 (these become RAG context for LLM) ===\n")
for i, r in enumerate(ranked[:5]):
    m = " *** 500시간 ***" if r["has_500"] else ""
    topk = " <-- SELECTED" if i < 3 else ""
    print(f"  #{i+1} boosted={r['boosted']:.4f} (orig={r['orig']:.4f}, +{len(r['matches'])}kw) {r['doc']}:chunk{r['idx']}{m}{topk}")
    print(f"     kw: {r['matches']}")
    print(f"     {r['header']}")
    print()
