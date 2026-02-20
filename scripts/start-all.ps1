# start-all.ps1
# FabCopilot 인프라 + .NET 서비스 전체 일괄 시작 스크립트

$root = "D:\__WORK2__\LLM-Chat-Service-master"

# ============================================
# Phase 1: 인프라 시작
# ============================================
Write-Host "=== Phase 1: Infrastructure ===" -ForegroundColor Cyan
Write-Host ""

& "$root\scripts\start-infra.ps1"

# 인프라 포트 확인
$infraPorts = @(4222, 6379, 6333, 11434)
$infraOk = $true
foreach ($port in $infraPorts) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $port)
        $tcp.Close()
    } catch {
        $infraOk = $false
    }
}

if (-not $infraOk) {
    Write-Host ""
    Write-Host "ERROR: Infrastructure not fully ready. Fix the issues above before continuing." -ForegroundColor Red
    Write-Host "Aborting .NET service startup." -ForegroundColor Red
    exit 1
}

# ============================================
# Phase 2: 솔루션 빌드
# ============================================
Write-Host ""
Write-Host "=== Phase 2: Build ===" -ForegroundColor Cyan
Write-Host ""

dotnet build "$root\FabCopilot.sln" --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed. Fix build errors before continuing." -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded" -ForegroundColor Green

# ============================================
# Phase 3: .NET 서비스 시작
# ============================================
Write-Host ""
Write-Host "=== Phase 3: Services ===" -ForegroundColor Cyan
Write-Host ""

$services = [ordered]@{
    "ChatGateway"      = "src\Services\FabCopilot.ChatGateway"
    "LlmService"       = "src\Services\FabCopilot.LlmService"
    "KnowledgeService" = "src\Services\FabCopilot.KnowledgeService"
    "RagService"       = "src\Services\FabCopilot.RagService"
    "AlarmCopilot"     = "src\Services\FabCopilot.AlarmCopilot"
    "McpLogServer"     = "src\Services\FabCopilot.McpLogServer"
    "RcaAgent"         = "src\Services\FabCopilot.RcaAgent"
    "WebClient"        = "src\Client\FabCopilot.WebClient"
}

$i = 0
foreach ($entry in $services.GetEnumerator()) {
    $i++
    $name = $entry.Key
    $project = $entry.Value

    Write-Host "[$i/$($services.Count)] Starting $name..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "Set-Location '$root'; dotnet run --project '$project' --no-build" `
        -WindowStyle Normal
    Start-Sleep -Seconds 2
}

# ============================================
# Phase 4: 최종 확인
# ============================================
Write-Host ""
Write-Host "=== Phase 4: Health Check ===" -ForegroundColor Cyan
Start-Sleep -Seconds 5

$endpoints = [ordered]@{
    "NATS (4222)"        = 4222
    "Redis (6379)"       = 6379
    "Qdrant (6333)"      = 6333
    "Ollama (11434)"     = 11434
    "ChatGateway (5000)" = 5000
    "WebClient (5010)"   = 5010
}

foreach ($ep in $endpoints.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $ep.Value)
        $tcp.Close()
        Write-Host "  $($ep.Key): OK" -ForegroundColor Green
    } catch {
        Write-Host "  $($ep.Key): FAIL" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== FabCopilot Ready ===" -ForegroundColor Cyan
Write-Host "  WebClient:  http://localhost:5010" -ForegroundColor White
Write-Host "  Gateway WS: ws://localhost:5000/ws/chat" -ForegroundColor White
Write-Host ""
