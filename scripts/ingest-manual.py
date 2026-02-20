"""
Ingest Fab Copilot User Manual into Qdrant knowledge collection.
Chunks the document, generates embeddings via Ollama, and upserts to Qdrant.

Usage: python scripts/ingest-manual.py
"""

import hashlib
import json
import sys
import urllib.request
import urllib.error
from datetime import datetime

# ── Configuration ─────────────────────────────────────────

OLLAMA_URL = "http://localhost:11434"
QDRANT_URL = "http://localhost:6333"
COLLECTION = "knowledge"
EMBED_MODEL = "nomic-embed-text"
MANUAL_FILE = "Plan/Fab_Copilot_User_Manual.md"
DOC_ID = "fab-copilot-user-manual"
CHUNK_SIZE = 500
CHUNK_OVERLAP = 100


def log(msg: str):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {msg}")


def make_uuid(text: str) -> str:
    """Generate a deterministic UUID from a string using MD5."""
    h = hashlib.md5(text.encode()).hexdigest()
    return f"{h[:8]}-{h[8:12]}-{h[12:16]}-{h[16:20]}-{h[20:]}"


def http_json(url: str, data: dict | None = None, method: str | None = None) -> dict:
    """Simple HTTP JSON request."""
    if data is not None:
        body = json.dumps(data).encode()
        req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    else:
        req = urllib.request.Request(url)
    if method:
        req.method = method
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode())


def chunk_text(text: str, chunk_size: int = 500, overlap: int = 100) -> list[str]:
    """Split text into overlapping chunks, preferring section boundaries."""
    # Split by markdown sections first
    sections = []
    current = []
    for line in text.split("\n"):
        if line.startswith("## ") and current:
            sections.append("\n".join(current))
            current = [line]
        else:
            current.append(line)
    if current:
        sections.append("\n".join(current))

    # Further chunk sections that exceed chunk_size
    chunks = []
    for section in sections:
        if len(section) <= chunk_size:
            if section.strip():
                chunks.append(section.strip())
        else:
            step = chunk_size - overlap
            if step <= 0:
                step = 1
            offset = 0
            while offset < len(section):
                chunk = section[offset : offset + chunk_size].strip()
                if chunk:
                    chunks.append(chunk)
                offset += step
                if offset + overlap >= len(section):
                    break
    return chunks


def get_embedding(text: str) -> list[float]:
    """Get embedding vector from Ollama."""
    resp = http_json(f"{OLLAMA_URL}/api/embed", {"model": EMBED_MODEL, "input": [text]})
    return resp["embeddings"][0]


def upsert_point(point_id: str, vector: list[float], payload: dict):
    """Upsert a single point to Qdrant."""
    http_json(
        f"{QDRANT_URL}/collections/{COLLECTION}/points",
        {"points": [{"id": point_id, "vector": vector, "payload": payload}]},
        method="PUT",
    )


def main():
    # ── Pre-flight ───────────────────────────────────────
    log(f"Reading manual: {MANUAL_FILE}")
    try:
        with open(MANUAL_FILE, encoding="utf-8") as f:
            content = f.read()
    except FileNotFoundError:
        print(f"ERROR: File not found: {MANUAL_FILE}")
        sys.exit(1)

    try:
        http_json(f"{OLLAMA_URL}/api/tags")
        log("Ollama: OK")
    except Exception as e:
        print(f"ERROR: Ollama not reachable: {e}")
        sys.exit(1)

    try:
        info = http_json(f"{QDRANT_URL}/collections/{COLLECTION}")
        log(f"Qdrant collection '{COLLECTION}': OK (points={info['result']['points_count']})")
    except Exception as e:
        print(f"ERROR: Qdrant collection not found: {e}")
        sys.exit(1)

    # ── Chunk ────────────────────────────────────────────
    chunks = chunk_text(content, CHUNK_SIZE, CHUNK_OVERLAP)
    log(f"Text: {len(content)} chars → {len(chunks)} chunks")

    # ── Embed & upsert ──────────────────────────────────
    success = 0
    failed = 0

    for i, chunk in enumerate(chunks):
        chunk_id = f"{DOC_ID}:chunk:{i}"
        point_id = make_uuid(chunk_id)

        log(f"  [{i + 1}/{len(chunks)}] Embedding chunk {i} ({len(chunk)} chars)...")

        try:
            vector = get_embedding(chunk)
            payload = {
                "text": chunk,
                "document_id": DOC_ID,
                "document_title": "Fab Copilot User Manual",
                "chunk_index": i,
                "chunk_count": len(chunks),
                "source": MANUAL_FILE,
                "type": "user_manual",
            }
            upsert_point(point_id, vector, payload)
            success += 1
        except Exception as e:
            log(f"  WARN: Failed chunk {i}: {e}")
            failed += 1

    # ── Summary ──────────────────────────────────────────
    info = http_json(f"{QDRANT_URL}/collections/{COLLECTION}")
    points = info["result"]["points_count"]

    log("=" * 50)
    log("Ingestion complete!")
    log(f"  Total chunks:  {len(chunks)}")
    log(f"  Success:       {success}")
    log(f"  Failed:        {failed}")
    log(f"  Collection:    {COLLECTION}")
    log(f"  Document ID:   {DOC_ID}")
    log(f"  Qdrant points: {points}")


if __name__ == "__main__":
    main()
