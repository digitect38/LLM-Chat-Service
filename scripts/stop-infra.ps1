# stop-infra.ps1
# FabCopilot 인프라 서비스 일괄 중지 스크립트

Write-Host "=== FabCopilot Infrastructure Shutdown ===" -ForegroundColor Cyan
Write-Host ""

$services = [ordered]@{
    "nats-server"  = "NATS"
    "redis-server" = "Redis"
    "qdrant"       = "Qdrant"
}

foreach ($entry in $services.GetEnumerator()) {
    $proc = Get-Process -Name $entry.Key -ErrorAction SilentlyContinue
    if ($proc) {
        $proc | Stop-Process -Force
        Write-Host "  $($entry.Value): stopped (PID $($proc.Id))" -ForegroundColor Yellow
    } else {
        Write-Host "  $($entry.Value): not running" -ForegroundColor DarkGray
    }
}

# Ollama는 시스템 트레이에서 관리
Write-Host "  Ollama: skipped (managed via system tray)" -ForegroundColor DarkGray

# 포트 확인
Write-Host ""
Write-Host "=== Port Check ===" -ForegroundColor Cyan
Start-Sleep -Seconds 1

$ports = [ordered]@{
    "NATS"   = 4222
    "Redis"  = 6379
    "Qdrant" = 6333
}

foreach ($svc in $ports.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $svc.Value)
        $tcp.Close()
        Write-Host "  $($svc.Key) (port $($svc.Value)): still listening" -ForegroundColor Red
    } catch {
        Write-Host "  $($svc.Key) (port $($svc.Value)): closed" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Infrastructure stopped." -ForegroundColor Cyan
