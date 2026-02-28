#!/bin/bash
# start-all.sh — Docker infra + all 8 .NET services
# Usage: bash scripts/start-all.sh [--no-build] [--profile hf] [--profile whisper]
#
# Profiles:
#   (default)  nats, redis, qdrant, ollama
#   --profile hf       + tei, tgi (HuggingFace inference)
#   --profile whisper  + whisper  (Speech-to-Text)

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NO_BUILD=false
PROFILES=()

SKIP_PREFLIGHT=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)       NO_BUILD=true; shift ;;
    --no-preflight)   SKIP_PREFLIGHT=true; shift ;;
    --profile)        PROFILES+=("--profile" "$2"); shift 2 ;;
    *) echo "Unknown flag: $1"; exit 1 ;;
  esac
done

# ============================================
# Phase 1: Infrastructure (Docker Compose)
# ============================================
echo "=== Phase 1: Infrastructure ==="
echo ""
docker compose -f "$ROOT/infra/docker-compose.yml" "${PROFILES[@]+"${PROFILES[@]}"}" up -d
echo ""

# ============================================
# Phase 2: Wait for infra ports
# ============================================
echo "=== Phase 2: Waiting for infrastructure ==="

wait_for_port() {
  local name=$1 port=$2 retries=30
  for _ in $(seq 1 $retries); do
    if (echo > /dev/tcp/localhost/$port) 2>/dev/null; then
      echo "  $name ($port): ready"
      return 0
    fi
    sleep 0.5
  done
  echo "  $name ($port): TIMEOUT"
  return 1
}

# Wait for all ports in parallel
pids=()
wait_for_port "NATS"   4222  & pids+=($!)
wait_for_port "Redis"  6379  & pids+=($!)
wait_for_port "Qdrant" 6333  & pids+=($!)
wait_for_port "Ollama" 11434 & pids+=($!)

INFRA_OK=true
for pid in "${pids[@]}"; do
  wait "$pid" || INFRA_OK=false
done

if [ "$INFRA_OK" = false ]; then
  echo ""
  echo "ERROR: Infrastructure not fully ready. Aborting."
  exit 1
fi
echo ""

# ============================================
# Phase 2.5: GPU Pre-flight Check
# ============================================
if [ "$SKIP_PREFLIGHT" = false ]; then
  echo "=== Phase 2.5: GPU Pre-flight Check ==="
  echo ""
  bash "$ROOT/scripts/preflight-check.sh" --report "$ROOT/preflight-report.json" || true
  echo ""
else
  echo "=== Phase 2.5: Pre-flight Check (skipped --no-preflight) ==="
  echo ""
fi

# ============================================
# Phase 3: Build solution
# ============================================
if [ "$NO_BUILD" = false ]; then
  echo "=== Phase 3: Build ==="
  echo ""
  dotnet build "$ROOT/FabCopilot.sln" --nologo -v q
  if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
  fi
  echo "  Build succeeded"
  echo ""
else
  echo "=== Phase 3: Build (skipped --no-build) ==="
  echo ""
fi

# ============================================
# Phase 4: Start .NET services
# ============================================
echo "=== Phase 4: Services ==="
echo ""

SERVICES=(
  "src/Services/FabCopilot.ChatGateway"
  "src/Services/FabCopilot.LlmService"
  "src/Services/FabCopilot.KnowledgeService"
  "src/Services/FabCopilot.RagService"
  "src/Services/FabCopilot.AlarmCopilot"
  "src/Services/FabCopilot.McpLogServer"
  "src/Services/FabCopilot.RcaAgent"
  "src/Client/FabCopilot.WebClient"
)

TOTAL=${#SERVICES[@]}
for i in "${!SERVICES[@]}"; do
  svc="${SERVICES[$i]}"
  name=$(basename "$svc")
  num=$((i + 1))
  echo "[$num/$TOTAL] Starting $name..."
  dotnet run --project "$ROOT/$svc" --no-build > /dev/null 2>&1 &
done
echo ""

# ============================================
# Phase 5: Health check
# ============================================
echo "=== Phase 5: Health Check ==="

# Wait for .NET services to bind their ports
wait_for_port "ChatGateway" 5000
wait_for_port "WebClient"   5010

check_port() {
  local name=$1 port=$2
  if (echo > /dev/tcp/localhost/$port) 2>/dev/null; then
    echo "  $name: OK"
  else
    echo "  $name: FAIL"
  fi
}
echo ""
check_port "NATS (4222)"        4222
check_port "Redis (6379)"       6379
check_port "Qdrant (6333)"      6333
check_port "Ollama (11434)"     11434
check_port "ChatGateway (5000)" 5000
check_port "WebClient (5010)"   5010

echo ""
echo "=== FabCopilot Ready ==="
echo "  WebClient:  http://localhost:5010"
echo "  Gateway WS: ws://localhost:5000/ws/chat"
echo ""
