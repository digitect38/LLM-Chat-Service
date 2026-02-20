# stop-all.ps1
# FabCopilot .NET 서비스 + 인프라 전체 일괄 중지 스크립트

# ============================================
# Phase 1: .NET 서비스 중지
# ============================================
Write-Host "=== Phase 1: Stopping .NET Services ===" -ForegroundColor Cyan
Write-Host ""

$dotnetServices = [ordered]@{
    "FabCopilot.WebClient"        = "WebClient"
    "FabCopilot.ChatGateway"      = "ChatGateway"
    "FabCopilot.LlmService"       = "LlmService"
    "FabCopilot.KnowledgeService" = "KnowledgeService"
    "FabCopilot.RagService"       = "RagService"
    "FabCopilot.AlarmCopilot"     = "AlarmCopilot"
    "FabCopilot.McpLogServer"     = "McpLogServer"
    "FabCopilot.RcaAgent"         = "RcaAgent"
}

foreach ($entry in $dotnetServices.GetEnumerator()) {
    $proc = Get-Process -Name $entry.Key -ErrorAction SilentlyContinue
    if ($proc) {
        $proc | Stop-Process -Force
        Write-Host "  $($entry.Value): stopped (PID $($proc.Id))" -ForegroundColor Yellow
    } else {
        Write-Host "  $($entry.Value): not running" -ForegroundColor DarkGray
    }
}

# ============================================
# Phase 2: 인프라 중지
# ============================================
Write-Host ""
Write-Host "=== Phase 2: Stopping Infrastructure ===" -ForegroundColor Cyan
Write-Host ""

& "$PSScriptRoot\stop-infra.ps1"

# ============================================
# Phase 3: 최종 확인
# ============================================
Write-Host ""
Write-Host "=== Final Port Check ===" -ForegroundColor Cyan
Start-Sleep -Seconds 1

$ports = [ordered]@{
    "ChatGateway" = 5000
    "WebClient"   = 5010
    "NATS"        = 4222
    "Redis"       = 6379
    "Qdrant"      = 6333
}

$allClosed = $true
foreach ($svc in $ports.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $svc.Value)
        $tcp.Close()
        Write-Host "  $($svc.Key) (port $($svc.Value)): still listening" -ForegroundColor Red
        $allClosed = $false
    } catch {
        Write-Host "  $($svc.Key) (port $($svc.Value)): closed" -ForegroundColor Green
    }
}

Write-Host ""
if ($allClosed) {
    Write-Host "All services stopped successfully." -ForegroundColor Cyan
} else {
    Write-Host "Some ports are still open. You may need to manually kill the processes." -ForegroundColor Red
}
