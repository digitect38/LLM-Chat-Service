#!/bin/bash
# start-services.sh — .NET 서비스 5개 일괄 시작 (인프라는 이미 실행 중 가정)
# Usage: bash scripts/start-services.sh [--build]

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

SERVICES=(
  "src/Services/FabCopilot.ChatGateway"
  "src/Services/FabCopilot.LlmService"
  "src/Services/FabCopilot.RagService"
  "src/Services/FabCopilot.KnowledgeService"
  "src/Client/FabCopilot.WebClient"
)

# Optional build
if [[ "$1" == "--build" ]]; then
  echo "=== Building solution ==="
  dotnet build "$ROOT/FabCopilot.sln" --nologo -v q
  if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
  fi
  echo "Build succeeded"
  echo ""
fi

# Start all services
for svc in "${SERVICES[@]}"; do
  name=$(basename "$svc")
  echo "Starting $name..."
  dotnet run --project "$ROOT/$svc" --no-build > /dev/null 2>&1 &
done

# Wait and verify
sleep 5
echo ""
echo "=== Process Check ==="
tasklist 2>/dev/null | grep -i "FabCopilot" || ps aux | grep -i "FabCopilot" | grep -v grep
echo ""
echo "=== Ready ==="
echo "  WebClient:  http://localhost:5010"
echo "  Gateway WS: ws://localhost:5000/ws/chat"
