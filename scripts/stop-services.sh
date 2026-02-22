#!/bin/bash
# stop-services.sh — .NET 서비스 5개 일괄 중지
# Usage: bash scripts/stop-services.sh

PROCS=(
  "FabCopilot.ChatGateway"
  "FabCopilot.LlmService"
  "FabCopilot.RagService"
  "FabCopilot.KnowledgeService"
  "FabCopilot.WebClient"
)

echo "=== Stopping Services ==="
for proc in "${PROCS[@]}"; do
  taskkill //F //IM "${proc}.exe" > /dev/null 2>&1
  if [ $? -eq 0 ]; then
    echo "  $proc: stopped"
  else
    echo "  $proc: not running"
  fi
done
echo "Done."
