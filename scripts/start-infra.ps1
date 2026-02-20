# start-infra.ps1
# FabCopilot 인프라 서비스 일괄 시작 스크립트

Write-Host "=== FabCopilot Infrastructure Startup ===" -ForegroundColor Cyan
Write-Host ""

# NATS
Write-Host "[1/4] Starting NATS Server..." -ForegroundColor Yellow
$natsExe = "D:\nats-server-v2.10.24-windows-amd64\nats-server.exe"
$natsConf = "D:\__WORK2__\LLM-Chat-Service-master\infra\nats\nats-server-windows.conf"

if (Get-Process -Name "nats-server" -ErrorAction SilentlyContinue) {
    Write-Host "       NATS already running, skipping" -ForegroundColor DarkYellow
} elseif (Test-Path $natsExe) {
    Start-Process -FilePath $natsExe -ArgumentList "-c", $natsConf -WindowStyle Minimized
    Write-Host "       NATS started" -ForegroundColor Green
} else {
    Write-Host "       ERROR: $natsExe not found" -ForegroundColor Red
}

# Redis
Write-Host "[2/4] Starting Redis..." -ForegroundColor Yellow
$redisExe = "D:\Redis\Redis-8.6.0-Windows-x64-msys2\redis-server.exe"

if (Get-Process -Name "redis-server" -ErrorAction SilentlyContinue) {
    Write-Host "       Redis already running, skipping" -ForegroundColor DarkYellow
} elseif (Test-Path $redisExe) {
    Start-Process -FilePath $redisExe `
        -ArgumentList "--appendonly", "yes", "--maxmemory", "2gb", "--maxmemory-policy", "allkeys-lru" `
        -WindowStyle Minimized
    Write-Host "       Redis started" -ForegroundColor Green
} else {
    Write-Host "       ERROR: $redisExe not found" -ForegroundColor Red
}

# Qdrant
Write-Host "[3/4] Starting Qdrant..." -ForegroundColor Yellow
$qdrantExe = "D:\Qdrant\qdrant.exe"

if (Get-Process -Name "qdrant" -ErrorAction SilentlyContinue) {
    Write-Host "       Qdrant already running, skipping" -ForegroundColor DarkYellow
} elseif (Test-Path $qdrantExe) {
    Start-Process -FilePath $qdrantExe -WindowStyle Minimized
    Write-Host "       Qdrant started" -ForegroundColor Green
} else {
    Write-Host "       ERROR: $qdrantExe not found" -ForegroundColor Red
}

# Ollama
Write-Host "[4/4] Checking Ollama..." -ForegroundColor Yellow
try {
    $null = Invoke-RestMethod http://localhost:11434/api/tags -TimeoutSec 3
    Write-Host "       Ollama already running" -ForegroundColor Green
} catch {
    Write-Host "       Ollama not responding. Please start Ollama from system tray." -ForegroundColor Red
}

# 포트 확인
Write-Host ""
Write-Host "=== Port Health Check ===" -ForegroundColor Cyan
Start-Sleep -Seconds 3

$services = [ordered]@{
    "NATS"   = 4222
    "Redis"  = 6379
    "Qdrant" = 6333
    "Ollama" = 11434
}

$allOk = $true
foreach ($svc in $services.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $svc.Value)
        $tcp.Close()
        Write-Host "  $($svc.Key) (port $($svc.Value)): OK" -ForegroundColor Green
    } catch {
        Write-Host "  $($svc.Key) (port $($svc.Value)): FAIL" -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "All infrastructure services are running!" -ForegroundColor Cyan
} else {
    Write-Host "Some services failed to start. Check the errors above." -ForegroundColor Red
}
