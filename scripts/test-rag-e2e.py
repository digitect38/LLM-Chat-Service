"""
End-to-end RAG verification test.
Tests each layer independently, then runs a full chat pipeline test.

Usage: python scripts/test-rag-e2e.py
"""

import asyncio
import json
import sys
import time
import urllib.request
import uuid

OLLAMA_URL = "http://localhost:11434"
QDRANT_URL = "http://localhost:6333"
GATEWAY_WS = "ws://localhost:5000/ws/chat/CMP01"
COLLECTION = "knowledge"

GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
BOLD = "\033[1m"
RESET = "\033[0m"


def ok(msg):
    print(f"  {GREEN}PASS{RESET} {msg}")


def fail(msg):
    print(f"  {RED}FAIL{RESET} {msg}")


def warn(msg):
    print(f"  {YELLOW}WARN{RESET} {msg}")


def http_json(url, data=None, method=None):
    if data is not None:
        body = json.dumps(data).encode()
        req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    else:
        req = urllib.request.Request(url)
    if method:
        req.method = method
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode())


def test_infrastructure():
    print(f"\n{BOLD}[1/4] Infrastructure Check{RESET}")

    # Ollama
    try:
        tags = http_json(f"{OLLAMA_URL}/api/tags")
        models = [m["name"] for m in tags["models"]]
        ok(f"Ollama: {len(models)} models")
        if "nomic-embed-text:latest" in models:
            ok("  nomic-embed-text installed")
        else:
            fail("  nomic-embed-text NOT installed")
            return False
    except Exception as e:
        fail(f"Ollama: {e}")
        return False

    # Qdrant
    try:
        info = http_json(f"{QDRANT_URL}/collections/{COLLECTION}")
        pts = info["result"]["points_count"]
        ok(f"Qdrant: {COLLECTION} collection, {pts} points")
        if pts == 0:
            fail("  No documents indexed!")
            return False
    except Exception as e:
        fail(f"Qdrant: {e}")
        return False

    # Gateway
    try:
        req = urllib.request.Request(f"http://localhost:5000/")
        urllib.request.urlopen(req, timeout=5)
        ok("ChatGateway: running")
    except urllib.error.HTTPError:
        ok("ChatGateway: running (404 = no root route, expected)")
    except Exception as e:
        fail(f"ChatGateway: {e}")
        return False

    return True


def test_embedding():
    print(f"\n{BOLD}[2/4] Embedding Test{RESET}")

    query = "모델 선택 드롭다운"
    try:
        t0 = time.time()
        resp = http_json(f"{OLLAMA_URL}/api/embed", {"model": "nomic-embed-text", "input": [query]})
        elapsed = time.time() - t0
        dim = len(resp["embeddings"][0])
        ok(f"Embedding: dim={dim}, took {elapsed:.2f}s")
        return resp["embeddings"][0]
    except Exception as e:
        fail(f"Embedding: {e}")
        return None


def test_vector_search(vector):
    print(f"\n{BOLD}[3/4] Vector Search Test{RESET}")

    try:
        t0 = time.time()
        resp = http_json(
            f"{QDRANT_URL}/collections/{COLLECTION}/points/query",
            {"query": vector, "limit": 3, "with_payload": True},
        )
        elapsed = time.time() - t0
        points = resp["result"]["points"]
        ok(f"Search: {len(points)} results, took {elapsed:.3f}s")

        has_relevant = False
        for i, pt in enumerate(points):
            score = pt["score"]
            text = pt["payload"].get("text", "")[:150].replace("\n", " ")
            has_text = bool(pt["payload"].get("text"))

            # Check if result is relevant to model selection
            keywords = ["모델", "model", "EXAONE", "드롭다운", "선택"]
            relevant = any(kw.lower() in text.lower() for kw in keywords)
            if relevant:
                has_relevant = True

            marker = f"{GREEN}*{RESET}" if relevant else " "
            print(f"  {marker} [{i+1}] score={score:.4f} text={'YES' if has_text else 'NO'} | {text[:80]}...")

        if has_relevant:
            ok("Relevant results found for model selection query")
        else:
            warn("No directly relevant results in top 3 (may need better chunking)")

        return True
    except Exception as e:
        fail(f"Search: {e}")
        return False


async def test_chat_pipeline():
    print(f"\n{BOLD}[4/4] Full Chat Pipeline (WebSocket → Gateway → NATS → LLM){RESET}")

    try:
        import websockets
    except ImportError:
        fail("websockets library not installed")
        return

    query = "Fab Copilot에서 LLM 모델을 선택하는 방법은? 드롭다운이 어디 있어?"
    conv_id = uuid.uuid4().hex

    try:
        print(f"  Connecting to {GATEWAY_WS}...")
        t0 = time.time()

        async with websockets.connect(GATEWAY_WS) as ws:
            t_connect = time.time() - t0
            ok(f"WebSocket connected ({t_connect:.2f}s)")

            request = {
                "conversationId": conv_id,
                "equipmentId": "CMP01",
                "userMessage": query,
                "modelId": "exaone3.5:7.8b",
                "context": None,
            }
            await ws.send(json.dumps(request))
            t_sent = time.time()
            print(f"  Query: \"{query}\"")
            print(f"  Waiting for response (RAG timeout=10s, then LLM streaming)...")
            print()

            full_response = ""
            chunk_count = 0
            first_token_time = None

            while True:
                try:
                    msg = await asyncio.wait_for(ws.recv(), timeout=180)
                    chunk = json.loads(msg)

                    if chunk.get("error"):
                        fail(f"Error from server: {chunk['error']}")
                        return

                    token = chunk.get("token", "")
                    if token:
                        if first_token_time is None:
                            first_token_time = time.time()
                        full_response += token
                        chunk_count += 1
                        sys.stdout.write(token)
                        sys.stdout.flush()

                    if chunk.get("isComplete"):
                        break
                except asyncio.TimeoutError:
                    fail("Timeout: no response in 180s")
                    return

            t_done = time.time()
            print()
            print()

            # Timing analysis
            ttft = first_token_time - t_sent if first_token_time else 0
            total = t_done - t_sent
            ok(f"Response complete: {chunk_count} chunks, {len(full_response)} chars")
            print(f"  Time to first token: {ttft:.1f}s (includes RAG retrieval)")
            print(f"  Total time: {total:.1f}s")

            if ttft > 12:
                warn(f"TTFT > 12s suggests RAG timeout (10s) occurred")
            elif ttft > 3:
                ok(f"TTFT {ttft:.1f}s suggests RAG retrieval completed successfully")

            # Check if response contains manual-specific content
            print()
            manual_keywords = ["드롭다운", "dropdown", "상단 패널", "equipment panel",
                               "EXAONE", "Model", "model-select"]
            found = [kw for kw in manual_keywords if kw.lower() in full_response.lower()]
            if found:
                ok(f"Response contains manual-specific terms: {found}")
                ok("RAG knowledge is being used in responses!")
            else:
                warn("Response does not contain manual-specific terms")
                warn("RAG may not be injecting context into LLM prompt")

    except Exception as e:
        fail(f"WebSocket test: {e}")


def main():
    print(f"{BOLD}{'=' * 60}{RESET}")
    print(f"{BOLD}  Fab Copilot RAG End-to-End Verification{RESET}")
    print(f"{BOLD}{'=' * 60}{RESET}")

    if not test_infrastructure():
        print(f"\n{RED}Infrastructure check failed. Fix issues above first.{RESET}")
        return

    vector = test_embedding()
    if vector is None:
        return

    test_vector_search(vector)

    asyncio.run(test_chat_pipeline())

    print(f"\n{BOLD}{'=' * 60}{RESET}")
    print(f"{BOLD}  Test Complete{RESET}")
    print(f"{BOLD}{'=' * 60}{RESET}")


if __name__ == "__main__":
    main()
