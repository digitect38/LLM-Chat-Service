#Requires -RunAsAdministrator
<#
.SYNOPSIS
    FabCopilot Tailscale VPN 설정 스크립트
.DESCRIPTION
    1. Tailscale 설치 (winget)
    2. Windows 방화벽 규칙 추가 (WebClient:5010, Gateway:5000)
    3. Tailscale 로그인 안내
    4. 접속 정보 출력
.USAGE
    관리자 권한 PowerShell에서 실행:
    powershell -ExecutionPolicy Bypass -File scripts\setup-tailscale.ps1
#>

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " FabCopilot Tailscale VPN Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ─── Step 1: Install Tailscale ────────────────────────────────────
$tailscaleExe = "C:\Program Files\Tailscale\tailscale.exe"

if (Test-Path $tailscaleExe) {
    Write-Host "[OK] Tailscale already installed." -ForegroundColor Green
    & $tailscaleExe version
} else {
    Write-Host "[1/4] Installing Tailscale via winget..." -ForegroundColor Yellow
    winget install Tailscale.Tailscale --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Tailscale installation failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] Tailscale installed." -ForegroundColor Green
}

Write-Host ""

# ─── Step 2: Firewall Rules ──────────────────────────────────────
Write-Host "[2/4] Configuring Windows Firewall rules..." -ForegroundColor Yellow

$rules = @(
    @{ Name = "VisualFactory-Home-3000";   Port = 3000; Desc = "VisualFactoryHome Landing Page" },
    @{ Name = "ChatVibe-Service-4000";     Port = 4000; Desc = "ChatVibe-Service (Chat + WebSocket)" },
    @{ Name = "FabCopilot-Gateway-5000";   Port = 5000; Desc = "FabCopilot ChatGateway (WebSocket)" },
    @{ Name = "FabCopilot-WebClient-5010"; Port = 5010; Desc = "FabCopilot WebClient (Blazor Server)" }
)

foreach ($rule in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  [SKIP] Firewall rule '$($rule.Name)' already exists." -ForegroundColor Gray
    } else {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $rule.Port `
            -Action Allow `
            -Profile Any `
            -Description $rule.Desc | Out-Null
        Write-Host "  [OK] Added firewall rule: $($rule.Name) (TCP $($rule.Port))" -ForegroundColor Green
    }
}

Write-Host ""

# ─── Step 3: Tailscale Login ─────────────────────────────────────
Write-Host "[3/4] Checking Tailscale status..." -ForegroundColor Yellow

# Refresh PATH to find tailscale
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

$tsStatus = $null
try {
    $tsStatus = & $tailscaleExe status --json 2>$null | ConvertFrom-Json
} catch {
    # Tailscale not running yet
}

if ($tsStatus -and $tsStatus.BackendState -eq "Running") {
    $tsIP = & $tailscaleExe ip -4 2>$null
    Write-Host "[OK] Tailscale is connected. IP: $tsIP" -ForegroundColor Green
} else {
    Write-Host "[INFO] Tailscale is not logged in." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Please complete login:" -ForegroundColor White
    Write-Host "  1. Open Tailscale from the system tray (taskbar)" -ForegroundColor White
    Write-Host "  2. Click 'Log in' and authenticate with your account" -ForegroundColor White
    Write-Host "  3. Or run: & '$tailscaleExe' up" -ForegroundColor White
    Write-Host ""
    Write-Host "  After login, re-run this script to see connection info." -ForegroundColor White
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Setup complete. Login required." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    exit 0
}

Write-Host ""

# ─── Step 4: Print Connection Info ────────────────────────────────
Write-Host "[4/4] Connection Information" -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " FabCopilot External Access (Tailscale)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$hostname = & $tailscaleExe status --json 2>$null | ConvertFrom-Json | Select-Object -ExpandProperty Self | Select-Object -ExpandProperty DNSName
$hostname = $hostname.TrimEnd('.')

Write-Host "  Tailscale IP:    $tsIP" -ForegroundColor White
Write-Host "  Tailscale DNS:   $hostname" -ForegroundColor White
Write-Host ""
Write-Host "  Home Page:       http://${hostname}:3000" -ForegroundColor Green
Write-Host "  ChatVibe:        http://${hostname}:4000" -ForegroundColor Green
Write-Host "  WebClient (UI):  http://${hostname}:5010" -ForegroundColor Green
Write-Host "  Gateway (WS):    ws://${hostname}:5000/ws/chat/{equipmentId}" -ForegroundColor Green
Write-Host ""
Write-Host "  (IP alternative: http://${tsIP}:3000, :4000, :5010)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Note: Connect from any device on your Tailscale network." -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan
