# health-check.ps1 - 전체 서비스 포트 상태 확인
param([int]$DelaySeconds = 0)

if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }

Write-Host "=== Service Health Check ===" -ForegroundColor Cyan

$endpoints = [ordered]@{
    "NATS"             = 4222
    "Redis"            = 6379
    "Qdrant"           = 6333
    "Ollama"           = 11434
    "ChatGateway"      = 5000
    "LlmService"       = 5001
    "KnowledgeService" = 5002
    "RagService"       = 5003
    "AlarmCopilot"     = 5004
    "McpLogServer"     = 5005
    "RcaAgent"         = 5006
    "WebClient"        = 5010
}

$allOk = $true
foreach ($ep in $endpoints.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $ep.Value)
        $tcp.Close()
        Write-Host "  $($ep.Key) ($($ep.Value)): OK" -ForegroundColor Green
    } catch {
        Write-Host "  $($ep.Key) ($($ep.Value)): FAIL" -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "All services running!" -ForegroundColor Cyan
} else {
    Write-Host "Some services are not running." -ForegroundColor Yellow
}
