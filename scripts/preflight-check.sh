#!/bin/bash
# preflight-check.sh — GPU & LLM inference pre-flight validation
# Usage: bash scripts/preflight-check.sh [--ollama-url URL] [--json] [--quiet]
#
# Steps:
#   1. GPU Detection (NVIDIA / Apple MPS / CPU-only)
#   2. CUDA Driver Version Check
#   3. VRAM → Optimal Model Size Recommendation
#   4. Inference Smoke Test (first-token latency, tokens/sec)
#   5. Report Generation (PASS/WARN/FAIL)
#
# Exit codes: 0=PASS, 1=FAIL, 2=WARN

set -euo pipefail

# ── Defaults ──────────────────────────────────────────────────────
OLLAMA_URL="${OLLAMA_URL:-http://localhost:11434}"
JSON_OUTPUT=false
QUIET=false
REPORT_FILE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --ollama-url) OLLAMA_URL="$2"; shift 2 ;;
    --json)       JSON_OUTPUT=true; shift ;;
    --quiet)      QUIET=true; shift ;;
    --report)     REPORT_FILE="$2"; shift 2 ;;
    *) echo "Unknown flag: $1"; exit 1 ;;
  esac
done

# ── Utilities ─────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log() { [[ "$QUIET" == "false" ]] && echo -e "$@"; }
pass() { log "${GREEN}[PASS]${NC} $1"; }
warn() { log "${YELLOW}[WARN]${NC} $1"; }
fail() { log "${RED}[FAIL]${NC} $1"; }

OVERALL="PASS"
WARNINGS=()
FAILURES=()

record_warn() { WARNINGS+=("$1"); [[ "$OVERALL" == "PASS" ]] && OVERALL="WARN"; }
record_fail() { FAILURES+=("$1"); OVERALL="FAIL"; }

# ── Result variables ──────────────────────────────────────────────
GPU_DETECTED="none"
GPU_NAME=""
GPU_DRIVER=""
CUDA_VERSION=""
VRAM_MB=0
RECOMMENDED_PROFILE="cpu_only"
RECOMMENDED_MODELS=""
SMOKE_FIRST_TOKEN_MS=0
SMOKE_TOKENS_PER_SEC=0
SMOKE_MODEL=""

# ══════════════════════════════════════════════════════════════════
# Step 1: GPU Detection
# ══════════════════════════════════════════════════════════════════
log ""
log "╔══════════════════════════════════════════════════╗"
log "║  Fab Copilot Pre-flight Check                   ║"
log "╚══════════════════════════════════════════════════╝"
log ""
log "=== Step 1: GPU Detection ==="

if command -v nvidia-smi &>/dev/null; then
  GPU_INFO=$(nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits 2>/dev/null || true)
  if [[ -n "$GPU_INFO" ]]; then
    GPU_DETECTED="nvidia"
    GPU_NAME=$(echo "$GPU_INFO" | head -1 | cut -d',' -f1 | xargs)
    VRAM_MB=$(echo "$GPU_INFO" | head -1 | cut -d',' -f2 | xargs)
    GPU_DRIVER=$(echo "$GPU_INFO" | head -1 | cut -d',' -f3 | xargs)
    pass "NVIDIA GPU detected: $GPU_NAME (${VRAM_MB} MB VRAM)"
  fi
elif [[ "$(uname)" == "Darwin" ]]; then
  # Check Apple Silicon MPS
  if sysctl -n machdep.cpu.brand_string 2>/dev/null | grep -qi "apple"; then
    GPU_DETECTED="apple_mps"
    GPU_NAME=$(sysctl -n machdep.cpu.brand_string 2>/dev/null || echo "Apple Silicon")
    # Unified memory — report total system RAM
    VRAM_MB=$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1024 / 1024 ))
    pass "Apple Silicon detected: $GPU_NAME (${VRAM_MB} MB unified memory)"
  fi
fi

if [[ "$GPU_DETECTED" == "none" ]]; then
  warn "No GPU detected — CPU-only mode"
  record_warn "No GPU detected. LLM inference will be slow."
fi

# ══════════════════════════════════════════════════════════════════
# Step 2: CUDA Driver Version Check
# ══════════════════════════════════════════════════════════════════
log ""
log "=== Step 2: CUDA Driver Check ==="

if [[ "$GPU_DETECTED" == "nvidia" ]]; then
  CUDA_VERSION=$(nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>/dev/null | head -1 | xargs || true)

  if [[ -n "$CUDA_VERSION" ]]; then
    # Extract major version number
    DRIVER_MAJOR=$(echo "$CUDA_VERSION" | cut -d'.' -f1)

    if (( DRIVER_MAJOR >= 535 )); then
      pass "CUDA driver $CUDA_VERSION (supports CUDA 12.x)"
    elif (( DRIVER_MAJOR >= 525 )); then
      warn "CUDA driver $CUDA_VERSION — consider updating to 535+ for CUDA 12 support"
      record_warn "CUDA driver $CUDA_VERSION is outdated"
    else
      fail "CUDA driver $CUDA_VERSION is too old — minimum 525 recommended"
      record_fail "CUDA driver too old: $CUDA_VERSION"
    fi
  else
    warn "Could not determine CUDA driver version"
    record_warn "CUDA driver version unknown"
  fi
elif [[ "$GPU_DETECTED" == "apple_mps" ]]; then
  pass "Apple MPS — no CUDA driver needed"
else
  log "  Skipped (no NVIDIA GPU)"
fi

# ══════════════════════════════════════════════════════════════════
# Step 3: VRAM → Model Size Recommendation
# ══════════════════════════════════════════════════════════════════
log ""
log "=== Step 3: Model Size Recommendation ==="

if [[ "$GPU_DETECTED" == "nvidia" ]]; then
  if (( VRAM_MB >= 48000 )); then
    RECOMMENDED_PROFILE="high_end"
    RECOMMENDED_MODELS="70B-Q4 / 32B-FP16 / 13B-FP16"
    pass "High-end GPU (${VRAM_MB}MB) → $RECOMMENDED_MODELS"
  elif (( VRAM_MB >= 24000 )); then
    RECOMMENDED_PROFILE="standard"
    RECOMMENDED_MODELS="32B-Q4 / 13B-FP16 / 7B-FP16"
    pass "Standard GPU (${VRAM_MB}MB) → $RECOMMENDED_MODELS"
  elif (( VRAM_MB >= 8000 )); then
    RECOMMENDED_PROFILE="entry"
    RECOMMENDED_MODELS="7B-Q4 / 3B-FP16"
    pass "Entry GPU (${VRAM_MB}MB) → $RECOMMENDED_MODELS"
  else
    RECOMMENDED_PROFILE="entry"
    RECOMMENDED_MODELS="3B-Q4 / 1B"
    warn "Low VRAM (${VRAM_MB}MB) — limited to small models: $RECOMMENDED_MODELS"
    record_warn "Low VRAM: ${VRAM_MB}MB"
  fi
elif [[ "$GPU_DETECTED" == "apple_mps" ]]; then
  RECOMMENDED_PROFILE="apple"
  if (( VRAM_MB >= 32000 )); then
    RECOMMENDED_MODELS="32B-Q4 / 13B-FP16 / 7B-FP16"
    pass "Apple Silicon (${VRAM_MB}MB unified) → $RECOMMENDED_MODELS"
  elif (( VRAM_MB >= 16000 )); then
    RECOMMENDED_MODELS="13B-Q4 / 7B-FP16"
    pass "Apple Silicon (${VRAM_MB}MB unified) → $RECOMMENDED_MODELS"
  else
    RECOMMENDED_MODELS="7B-Q4 / 3B"
    warn "Apple Silicon (${VRAM_MB}MB unified) → limited to: $RECOMMENDED_MODELS"
  fi
else
  RECOMMENDED_PROFILE="cpu_only"
  RECOMMENDED_MODELS="3B-Q4 / 1B (CPU inference, expect slow response)"
  warn "CPU-only mode → $RECOMMENDED_MODELS"
fi

# ══════════════════════════════════════════════════════════════════
# Step 4: Inference Smoke Test
# ══════════════════════════════════════════════════════════════════
log ""
log "=== Step 4: Inference Smoke Test ==="

# Check if Ollama is reachable
OLLAMA_REACHABLE=false
if curl -sf "$OLLAMA_URL/api/tags" > /dev/null 2>&1; then
  OLLAMA_REACHABLE=true
  pass "Ollama server reachable at $OLLAMA_URL"
else
  warn "Ollama server not reachable at $OLLAMA_URL — skipping smoke test"
  record_warn "Ollama not reachable for smoke test"
fi

if [[ "$OLLAMA_REACHABLE" == "true" ]]; then
  # Find a suitable model for smoke test
  AVAILABLE_MODELS=$(curl -sf "$OLLAMA_URL/api/tags" 2>/dev/null | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    for m in data.get('models', []):
        print(m['name'])
except: pass
" 2>/dev/null || true)

  if [[ -n "$AVAILABLE_MODELS" ]]; then
    # Prefer small models for smoke test
    SMOKE_MODEL=""
    for candidate in "tinyllama" "phi" "qwen2.5:0.5b" "qwen2.5:1.5b" "qwen2.5:3b" "gemma:2b"; do
      if echo "$AVAILABLE_MODELS" | grep -qi "$candidate"; then
        SMOKE_MODEL=$(echo "$AVAILABLE_MODELS" | grep -i "$candidate" | head -1)
        break
      fi
    done

    # Fallback to first available model
    if [[ -z "$SMOKE_MODEL" ]]; then
      SMOKE_MODEL=$(echo "$AVAILABLE_MODELS" | head -1)
    fi

    log "  Testing with model: $SMOKE_MODEL"

    # Measure first-token latency and throughput
    SMOKE_START=$(date +%s%3N 2>/dev/null || python3 -c "import time; print(int(time.time()*1000))")

    SMOKE_RESPONSE=$(curl -sf "$OLLAMA_URL/api/generate" \
      -d "{\"model\": \"$SMOKE_MODEL\", \"prompt\": \"Say hello in one word.\", \"stream\": false}" \
      --max-time 60 2>/dev/null || echo "")

    SMOKE_END=$(date +%s%3N 2>/dev/null || python3 -c "import time; print(int(time.time()*1000))")

    if [[ -n "$SMOKE_RESPONSE" ]]; then
      SMOKE_TOTAL_MS=$(( SMOKE_END - SMOKE_START ))

      # Parse timing from Ollama response (nanoseconds)
      EVAL_DURATION_NS=$(echo "$SMOKE_RESPONSE" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    print(data.get('eval_duration', 0))
except: print(0)
" 2>/dev/null || echo "0")

      EVAL_COUNT=$(echo "$SMOKE_RESPONSE" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    print(data.get('eval_count', 0))
except: print(0)
" 2>/dev/null || echo "0")

      LOAD_DURATION_NS=$(echo "$SMOKE_RESPONSE" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    print(data.get('load_duration', 0))
except: print(0)
" 2>/dev/null || echo "0")

      PROMPT_EVAL_NS=$(echo "$SMOKE_RESPONSE" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    print(data.get('prompt_eval_duration', 0))
except: print(0)
" 2>/dev/null || echo "0")

      # Calculate metrics
      if (( EVAL_DURATION_NS > 0 && EVAL_COUNT > 0 )); then
        SMOKE_TOKENS_PER_SEC=$(python3 -c "print(round($EVAL_COUNT / ($EVAL_DURATION_NS / 1e9), 1))" 2>/dev/null || echo "0")
      fi

      SMOKE_FIRST_TOKEN_MS=$(python3 -c "print(round(($LOAD_DURATION_NS + $PROMPT_EVAL_NS) / 1e6))" 2>/dev/null || echo "$SMOKE_TOTAL_MS")

      pass "Smoke test completed: first-token=${SMOKE_FIRST_TOKEN_MS}ms, throughput=${SMOKE_TOKENS_PER_SEC} tok/s"

      # Quality thresholds
      if (( $(echo "$SMOKE_TOKENS_PER_SEC < 5" | bc -l 2>/dev/null || echo "0") )); then
        warn "Low throughput (${SMOKE_TOKENS_PER_SEC} tok/s) — consider a smaller model or GPU upgrade"
        record_warn "Low inference throughput: ${SMOKE_TOKENS_PER_SEC} tok/s"
      fi

      if (( SMOKE_FIRST_TOKEN_MS > 30000 )); then
        warn "High first-token latency (${SMOKE_FIRST_TOKEN_MS}ms)"
        record_warn "High first-token latency: ${SMOKE_FIRST_TOKEN_MS}ms"
      fi
    else
      fail "Smoke test failed — no response from model $SMOKE_MODEL"
      record_fail "Smoke test failed"
    fi
  else
    warn "No models available in Ollama — pull a model first"
    record_warn "No Ollama models available"
  fi
fi

# ══════════════════════════════════════════════════════════════════
# Step 5: Report Generation
# ══════════════════════════════════════════════════════════════════
log ""
log "=== Step 5: Report ==="
log ""

# Summary
case "$OVERALL" in
  PASS) log "${GREEN}══ OVERALL: PASS ══${NC}" ;;
  WARN) log "${YELLOW}══ OVERALL: WARN ══${NC}" ;;
  FAIL) log "${RED}══ OVERALL: FAIL ══${NC}" ;;
esac

if (( ${#WARNINGS[@]} > 0 )); then
  log ""
  log "Warnings:"
  for w in "${WARNINGS[@]}"; do
    log "  - $w"
  done
fi

if (( ${#FAILURES[@]} > 0 )); then
  log ""
  log "Failures:"
  for f in "${FAILURES[@]}"; do
    log "  - $f"
  done
fi

log ""
log "Hardware Profile: $RECOMMENDED_PROFILE"
log "Recommended Models: $RECOMMENDED_MODELS"
log ""

# ── JSON Report ───────────────────────────────────────────────────
JSON_REPORT=$(cat <<ENDJSON
{
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "overall": "$OVERALL",
  "gpu": {
    "detected": "$GPU_DETECTED",
    "name": "$GPU_NAME",
    "driver_version": "$GPU_DRIVER",
    "cuda_version": "$CUDA_VERSION",
    "vram_mb": $VRAM_MB
  },
  "hardware_profile": "$RECOMMENDED_PROFILE",
  "recommended_models": "$RECOMMENDED_MODELS",
  "smoke_test": {
    "model": "$SMOKE_MODEL",
    "first_token_ms": $SMOKE_FIRST_TOKEN_MS,
    "tokens_per_sec": $SMOKE_TOKENS_PER_SEC
  },
  "warnings": $(printf '%s\n' "${WARNINGS[@]+"${WARNINGS[@]}"}" | python3 -c "
import sys, json
lines = [l.strip() for l in sys.stdin if l.strip()]
print(json.dumps(lines))
" 2>/dev/null || echo "[]"),
  "failures": $(printf '%s\n' "${FAILURES[@]+"${FAILURES[@]}"}" | python3 -c "
import sys, json
lines = [l.strip() for l in sys.stdin if l.strip()]
print(json.dumps(lines))
" 2>/dev/null || echo "[]")
}
ENDJSON
)

if [[ "$JSON_OUTPUT" == "true" ]]; then
  echo "$JSON_REPORT"
fi

if [[ -n "$REPORT_FILE" ]]; then
  echo "$JSON_REPORT" > "$REPORT_FILE"
  log "Report saved to: $REPORT_FILE"
fi

# Exit code
case "$OVERALL" in
  PASS) exit 0 ;;
  WARN) exit 2 ;;
  FAIL) exit 1 ;;
esac
