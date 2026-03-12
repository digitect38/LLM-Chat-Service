# start-infra.ps1
# FabCopilot 인프라 서비스 일괄 시작 스크립트

Write-Host "=== FabCopilot Infrastructure Startup ===" -ForegroundColor Cyan
Write-Host ""

# NATS
Write-Host "[1/4] Starting NATS Server..." -ForegroundColor Yellow
$root = (Resolve-Path "$PSScriptRoot\..").Path
$infraDir = Join-Path $root "infra"
$natsConf = Join-Path $infraDir "nats\nats-server-windows.conf"

# Try native executables first, then docker-compose
$natsExe  = Get-Command nats-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
$redisExe = Get-Command redis-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
$qdrantExe = Get-Command qdrant -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source

$useDocker = (-not $natsExe -or -not $redisExe -or -not $qdrantExe)

if ($useDocker) {
    $dockerOk = Get-Command docker -ErrorAction SilentlyContinue
    $composeFile = Join-Path $infraDir "docker-compose.yml"

    # Verify Docker daemon is actually running (not just installed)
    $dockerDaemonOk = $false
    if ($dockerOk) {
        docker info *> $null 2>&1
        $dockerDaemonOk = ($LASTEXITCODE -eq 0)
    }

    if ($dockerDaemonOk -and (Test-Path $composeFile)) {
        Write-Host "       Native executables not found. Using docker-compose..." -ForegroundColor DarkYellow

        # Check if containers are already running
        $running = docker compose -f $composeFile ps --status running -q 2>$null
        if ($running) {
            Write-Host "       Docker containers already running, skipping" -ForegroundColor DarkYellow
        } else {
            docker compose -f $composeFile up -d nats redis qdrant
            if ($LASTEXITCODE -eq 0) {
                Write-Host "       Docker containers started (nats, redis, qdrant)" -ForegroundColor Green
            } else {
                Write-Host "       ERROR: docker compose up failed (exit code $LASTEXITCODE)" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "       WARNING: Neither native executables nor Docker daemon available." -ForegroundColor Red
        Write-Host "       Options:" -ForegroundColor Red
        Write-Host "         1. Start Docker Desktop" -ForegroundColor Red
        Write-Host "         2. Install nats-server, redis-server, qdrant natively" -ForegroundColor Red
    }
} else {
    # Native executables path
    if (Get-Process -Name "nats-server" -ErrorAction SilentlyContinue) {
        Write-Host "       NATS already running, skipping" -ForegroundColor DarkYellow
    } else {
        Start-Process -FilePath $natsExe -ArgumentList "-c", $natsConf -WindowStyle Minimized
        Write-Host "       NATS started" -ForegroundColor Green
    }

    # Redis
    Write-Host "[2/4] Starting Redis..." -ForegroundColor Yellow
    if (Get-Process -Name "redis-server" -ErrorAction SilentlyContinue) {
        Write-Host "       Redis already running, skipping" -ForegroundColor DarkYellow
    } else {
        Start-Process -FilePath $redisExe `
            -ArgumentList "--appendonly", "yes", "--maxmemory", "2gb", "--maxmemory-policy", "allkeys-lru" `
            -WindowStyle Minimized
        Write-Host "       Redis started" -ForegroundColor Green
    }

    # Qdrant
    Write-Host "[3/4] Starting Qdrant..." -ForegroundColor Yellow
    if (Get-Process -Name "qdrant" -ErrorAction SilentlyContinue) {
        Write-Host "       Qdrant already running, skipping" -ForegroundColor DarkYellow
    } else {
        Start-Process -FilePath $qdrantExe -WindowStyle Minimized
        Write-Host "       Qdrant started" -ForegroundColor Green
    }
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
