# _register-autostart.ps1
# Windows Task Scheduler에 FabCopilot 자동 시작 작업 등록
# 관리자 권한 필요

param(
    [switch]$Unregister    # -Unregister: 등록 해제
)

$taskName = "FabCopilot-AutoStart"
$root = Split-Path -Parent $PSScriptRoot
$scriptPath = "$root\scripts\_autostart.ps1"

if ($Unregister) {
    Write-Host "Unregistering task '$taskName'..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Done. Task removed." -ForegroundColor Green
    exit 0
}

# 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Administrator privileges required." -ForegroundColor Red
    Write-Host "Run this script as Administrator:" -ForegroundColor Yellow
    Write-Host "  Right-click PowerShell -> Run as Administrator" -ForegroundColor White
    Write-Host "  Then: .\scripts\_register-autostart.ps1" -ForegroundColor White
    exit 1
}

# 기존 작업 확인/제거
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing task '$taskName'..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

# Trigger: 사용자 로그온 시
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

# Action: PowerShell로 _autostart.ps1 실행
$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -WindowStyle Normal -File `"$scriptPath`"" `
    -WorkingDirectory $root

# Settings
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) `
    -RestartCount 1 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# 등록 (현재 사용자, 최고 권한)
Register-ScheduledTask `
    -TaskName $taskName `
    -Description "FabCopilot full-stack auto start (Docker Desktop + Infrastructure + .NET Services)" `
    -Trigger $trigger `
    -Action $action `
    -Settings $settings `
    -RunLevel Highest `
    -Force

Write-Host ""
Write-Host "=== Task Registered ===" -ForegroundColor Green
Write-Host "  Task Name:  $taskName" -ForegroundColor White
Write-Host "  Trigger:    At logon ($env:USERNAME)" -ForegroundColor White
Write-Host "  Script:     $scriptPath" -ForegroundColor White
Write-Host "  Run Level:  Highest (elevated)" -ForegroundColor White
Write-Host ""
Write-Host "To test now:  schtasks /run /tn `"$taskName`"" -ForegroundColor Cyan
Write-Host "To remove:    .\scripts\_register-autostart.ps1 -Unregister" -ForegroundColor Cyan
Write-Host ""
