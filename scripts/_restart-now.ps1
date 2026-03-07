# Restart script — kill all .NET services and restart as background processes (no extra windows)
# -ExcludeDashboard: skip ServiceDashboard stop/start (used when invoked FROM Dashboard)
param(
    [switch]$ExcludeDashboard
)

$root = Split-Path -Parent $PSScriptRoot   # auto-detect from script location
$logDir = "$root\logs"
if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

Write-Host "`n=== Stopping services ===" -ForegroundColor Yellow
$names = @(
    "FabCopilot.WebClient",
    "FabCopilot.ChatGateway",
    "FabCopilot.LlmService",
    "FabCopilot.KnowledgeService",
    "FabCopilot.RagService"
)
if (-not $ExcludeDashboard) {
    $names += "FabCopilot.ServiceDashboard"
}

foreach ($n in $names) {
    $p = Get-Process -Name $n -ErrorAction SilentlyContinue
    if ($p) {
        Stop-Process -Name $n -Force
        Write-Host "  Stopped $n (PID $($p.Id))" -ForegroundColor Red
    } else {
        Write-Host "  ${n}: not running" -ForegroundColor DarkGray
    }
}

Start-Sleep -Seconds 2

Write-Host "`n=== Building solution ===" -ForegroundColor Yellow
dotnet build "$root\FabCopilot.sln" --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded" -ForegroundColor Green

Write-Host "`n=== Starting services (background, logs in /logs) ===" -ForegroundColor Yellow

$projects = [ordered]@{
    "ChatGateway"      = "src\Services\FabCopilot.ChatGateway"
    "LlmService"       = "src\Services\FabCopilot.LlmService"
    "KnowledgeService" = "src\Services\FabCopilot.KnowledgeService"
    "RagService"       = "src\Services\FabCopilot.RagService"
    "WebClient"        = "src\Client\FabCopilot.WebClient"
}
if (-not $ExcludeDashboard) {
    $projects["ServiceDashboard"] = "src\Client\FabCopilot.ServiceDashboard"
}

foreach ($entry in $projects.GetEnumerator()) {
    $name = $entry.Key
    $path = $entry.Value
    $logFile = "$logDir\$name.log"
    Start-Process -FilePath "dotnet" `
        -ArgumentList "run","--project","$root\$path","--no-build" `
        -WorkingDirectory $root `
        -WindowStyle Hidden `
        -RedirectStandardOutput $logFile `
        -RedirectStandardError "$logDir\$name.err.log"
    Write-Host "  Started $name  ->  logs\$name.log" -ForegroundColor Green
    Start-Sleep -Seconds 1
}

Write-Host "`n=== Waiting for startup ===" -ForegroundColor Yellow
Start-Sleep -Seconds 6

$ports = [ordered]@{
    "ChatGateway (5000)"      = 5000
    "WebClient (5010)"        = 5010
    "ServiceDashboard (5020)" = 5020
}

foreach ($ep in $ports.GetEnumerator()) {
    try {
        $tcp = New-Object Net.Sockets.TcpClient("localhost", $ep.Value)
        $tcp.Close()
        Write-Host "  $($ep.Key): OK" -ForegroundColor Green
    } catch {
        Write-Host "  $($ep.Key): NOT READY (may need more time)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Done (0 extra windows) ===" -ForegroundColor Green
Write-Host "  WebClient:  http://localhost:5010"
Write-Host "  Dashboard:  http://localhost:5020"
Write-Host "  Logs:       $logDir\"
