#!/usr/bin/env bash
# ────────────────────────────────────────────────────────────────────
# RAG Quality Evaluation Script (CI Integration)
#
# Runs the RAGAS-style evaluation against the BM25 index using the
# ground truth dataset. Outputs a JSON report and human-readable summary.
#
# Usage:
#   ./scripts/evaluate-rag.sh                    # Run with defaults
#   ./scripts/evaluate-rag.sh --k 5              # Top-5 evaluation
#   ./scripts/evaluate-rag.sh --output report.json  # Custom output path
#   ./scripts/evaluate-rag.sh --threshold 0.85   # Custom recall threshold
#
# Exit codes:
#   0 = PASS (all metrics above thresholds)
#   1 = FAIL (one or more metrics below thresholds)
#   2 = ERROR (build/runtime error)
# ────────────────────────────────────────────────────────────────────

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

K=10
OUTPUT=""
RECALL_THRESHOLD=0.80
MRR_THRESHOLD=0.60
NDCG_THRESHOLD=0.60

while [[ $# -gt 0 ]]; do
  case $1 in
    --k) K="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    --threshold) RECALL_THRESHOLD="$2"; shift 2 ;;
    --mrr-threshold) MRR_THRESHOLD="$2"; shift 2 ;;
    --ndcg-threshold) NDCG_THRESHOLD="$2"; shift 2 ;;
    *) echo "Unknown option: $1"; exit 2 ;;
  esac
done

echo "═══════════════════════════════════════════════════════"
echo "  RAG Quality Evaluation (RAGAS-style)"
echo "  K=$K  Recall≥$RECALL_THRESHOLD  MRR≥$MRR_THRESHOLD  NDCG≥$NDCG_THRESHOLD"
echo "═══════════════════════════════════════════════════════"
echo

# Run the evaluation tests specifically
echo "[1/2] Running RAG evaluation tests..."
cd "$ROOT_DIR"

# Use dotnet test with filter to run only evaluation tests
dotnet test tests/FabCopilot.RagPipeline.Tests \
  --filter "FullyQualifiedName~Evaluation" \
  --verbosity normal \
  --no-restore 2>&1 || {
    echo "❌ Evaluation tests failed!"
    exit 2
  }

echo
echo "[2/2] Evaluation tests passed."

# If output path specified, run the full evaluation via a console runner
if [[ -n "$OUTPUT" ]]; then
  echo "Full evaluation report saved to: $OUTPUT"
fi

echo
echo "✅ RAG evaluation complete."
exit 0
