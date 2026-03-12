# _autostart.ps1
# Windows 시작 시 전체 FabCopilot 스택 자동 시작
# Phase 1: Docker Desktop → Phase 2: Docker Compose (인프라) → Phase 3: .NET 서비스

param(
    [int]$DockerTimeout = 120,    # Docker Engine ready 대기 (초)
    [int]$InfraTimeout  = 90     # 컨테이너 ready 대기 (초)
)

$root = Split-Path -Parent $PSScriptRoot
$infraDir = "$root\infra"
$logDir = "$root\logs"
$logFile = "$logDir\_autostart.log"

if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

function Log {
    param([string]$Message, [string]$Color = "White")
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$ts] $Message"
    Write-Host $line -ForegroundColor $Color
    Add-Content -Path $logFile -Value $line
}

Log "============================================" "Cyan"
Log "FabCopilot AutoStart" "Cyan"
Log "============================================" "Cyan"

# ============================================
# Phase 1: Docker Desktop
# ============================================
Log ""
Log "=== Phase 1: Docker Desktop ===" "Yellow"

$dockerDesktop = "C:\Program Files\Docker\Docker\Docker Desktop.exe"

if (!(Test-Path $dockerDesktop)) {
    Log "ERROR: Docker Desktop not found at $dockerDesktop" "Red"
    exit 1
}

# Docker Engine이 이미 실행 중인지 확인
$dockerReady = $false
try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -eq 0) {
        $dockerReady = $true
        Log "Docker Engine already running" "Green"
    }
} catch { }

if (-not $dockerReady) {
    Log "Starting Docker Desktop..."
    Start-Process $dockerDesktop
    Log "Waiting for Docker Engine (max ${DockerTimeout}s)..."

    $elapsed = 0
    while ($elapsed -lt $DockerTimeout) {
        Start-Sleep -Seconds 5
        $elapsed += 5
        try {
            $null = docker info 2>&1
            if ($LASTEXITCODE -eq 0) {
                $dockerReady = $true
                break
            }
        } catch { }
        if ($elapsed % 15 -eq 0) {
            Log "  ... waiting (${elapsed}s)" "DarkGray"
        }
    }

    if ($dockerReady) {
        Log "Docker Engine ready (${elapsed}s)" "Green"
    } else {
        Log "ERROR: Docker Engine did not start within ${DockerTimeout}s" "Red"
        exit 1
    }
}

# ============================================
# Phase 2: Docker Compose (인프라 컨테이너)
# ============================================
Log ""
Log "=== Phase 2: Infrastructure Containers ===" "Yellow"

# 기본 서비스: NATS, Redis, Qdrant, Ollama
Log "Starting containers (docker compose up -d)..."
docker compose -f "$infraDir\docker-compose.yml" up -d 2>&1 | ForEach-Object { Log "  $_" "DarkGray" }

if ($LASTEXITCODE -ne 0) {
    Log "ERROR: docker compose up failed" "Red"
    exit 1
}

# 인프라 포트 health check
Log "Waiting for infrastructure (max ${InfraTimeout}s)..."

$infraPorts = [ordered]@{
    "NATS"   = 4222
    "Redis"  = 6379
    "Qdrant" = 6333
    "Ollama" = 11434
}

$elapsed = 0
$allReady = $false
while ($elapsed -lt $InfraTimeout) {
    Start-Sleep -Seconds 5
    $elapsed += 5

    $allReady = $true
    foreach ($svc in $infraPorts.GetEnumerator()) {
        try {
            $tcp = New-Object Net.Sockets.TcpClient("localhost", $svc.Value)
            $tcp.Close()
        } catch {
            $allReady = $false
            break
        }
    }

    if ($allReady) { break }
    if ($elapsed % 15 -eq 0) {
        Log "  ... waiting (${elapsed}s)" "DarkGray"
    }
}

if ($allReady) {
    Log "All infrastructure ready (${elapsed}s)" "Green"
    foreach ($svc in $infraPorts.GetEnumerator()) {
        Log "  $($svc.Key) (port $($svc.Value)): OK" "Green"
    }
} else {
    Log "WARNING: Some infrastructure not ready after ${InfraTimeout}s" "Yellow"
    foreach ($svc in $infraPorts.GetEnumerator()) {
        try {
            $tcp = New-Object Net.Sockets.TcpClient("localhost", $svc.Value)
            $tcp.Close()
            Log "  $($svc.Key) (port $($svc.Value)): OK" "Green"
        } catch {
            Log "  $($svc.Key) (port $($svc.Value)): FAIL" "Red"
        }
    }
    Log "Continuing with .NET service startup anyway..." "Yellow"
}

# Ollama 모델 확인
Log ""
Log "Checking Ollama models..."
try {
    $models = docker exec $(docker ps -qf "name=ollama" --no-trunc | Select-Object -First 1) ollama list 2>&1
    $models | ForEach-Object { Log "  $_" "DarkGray" }
} catch {
    Log "  Could not list models (container may still be initializing)" "Yellow"
}

# ============================================
# Phase 3: .NET 서비스 (_restart-now.ps1)
# ============================================
Log ""
Log "=== Phase 3: .NET Services ===" "Yellow"

$restartScript = "$root\scripts\_restart-now.ps1"

if (!(Test-Path $restartScript)) {
    Log "ERROR: _restart-now.ps1 not found" "Red"
    exit 1
}

Log "Running _restart-now.ps1..."
& $restartScript

# ============================================
# Final Summary
# ============================================
Log ""
Log "============================================" "Cyan"
Log "FabCopilot AutoStart Complete" "Cyan"
Log "============================================" "Cyan"
Log "  WebClient:  http://localhost:5010"
Log "  Dashboard:  http://localhost:5020"
Log "  Ollama:     http://localhost:11434"
Log "  Log file:   $logFile"
Log ""
