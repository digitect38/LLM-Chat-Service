Write-Host "`n=== Phase 1: Stop .NET services ===" -ForegroundColor Yellow

$services = @(
    "FabCopilot.ChatGateway",
    "FabCopilot.LlmService",
    "FabCopilot.KnowledgeService",
    "FabCopilot.RagService",
    "FabCopilot.AlarmCopilot",
    "FabCopilot.McpLogServer",
    "FabCopilot.RcaAgent",
    "FabCopilot.WebClient"
)

foreach ($svc in $services) {
    $proc = Get-Process -Name $svc -ErrorAction SilentlyContinue
    if ($proc) {
        Stop-Process -Name $svc -Force
        Write-Host "  Stopped $svc (PID $($proc.Id))" -ForegroundColor Red
    } else {
        Write-Host "  $svc not running" -ForegroundColor Gray
    }
}

Start-Sleep -Seconds 2

Write-Host "`n=== Phase 2: Delete Qdrant 'knowledge' collection (old chunks) ===" -ForegroundColor Yellow
try {
    $resp = Invoke-RestMethod -Uri "http://localhost:6333/collections/knowledge" -Method Delete -TimeoutSec 5
    Write-Host "  Deleted collection 'knowledge': $($resp.status)" -ForegroundColor Green
} catch {
    Write-Host "  Failed to delete collection: $($_.Exception.Message)" -ForegroundColor Red
}

Start-Sleep -Seconds 1

Write-Host "`n=== Phase 3: Build solution ===" -ForegroundColor Yellow
$root = "D:\__WORK2__\LLM-Chat-Service-master"
$buildResult = & dotnet build "$root\FabCopilot.sln" --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Build FAILED" -ForegroundColor Red
    $buildResult | Select-Object -Last 10 | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "  Build succeeded" -ForegroundColor Green

Write-Host "`n=== Phase 4: Start services ===" -ForegroundColor Yellow

$projects = @(
    @{ Name = "ChatGateway";      Path = "$root\src\Services\FabCopilot.ChatGateway" },
    @{ Name = "LlmService";       Path = "$root\src\Services\FabCopilot.LlmService" },
    @{ Name = "KnowledgeService"; Path = "$root\src\Services\FabCopilot.KnowledgeService" },
    @{ Name = "RagService";       Path = "$root\src\Services\FabCopilot.RagService" },
    @{ Name = "AlarmCopilot";     Path = "$root\src\Services\FabCopilot.AlarmCopilot" },
    @{ Name = "McpLogServer";     Path = "$root\src\Services\FabCopilot.McpLogServer" },
    @{ Name = "RcaAgent";         Path = "$root\src\Services\FabCopilot.RcaAgent" },
    @{ Name = "WebClient";        Path = "$root\src\Services\FabCopilot.WebClient" }
)

foreach ($proj in $projects) {
    Start-Process powershell -ArgumentList "-Command", "dotnet run --project `"$($proj.Path)`" --no-build" -WindowStyle Minimized
    Write-Host "  Started $($proj.Name)" -ForegroundColor Green
    Start-Sleep -Seconds 2
}

Write-Host "`n=== Phase 5: Wait for startup & verify ===" -ForegroundColor Yellow
Start-Sleep -Seconds 8

# Check Qdrant collection recreation
try {
    $coll = Invoke-RestMethod -Uri "http://localhost:6333/collections/knowledge" -Method Get -TimeoutSec 5
    $points = $coll.result.points_count
    Write-Host "  Qdrant 'knowledge' collection: $points points" -ForegroundColor Cyan
} catch {
    Write-Host "  Qdrant collection not yet created (RagService may still be ingesting)" -ForegroundColor Yellow
}

# Check key service ports
$ports = @(
    @{ Name = "ChatGateway"; Port = 5000 },
    @{ Name = "KnowledgeService"; Port = 5002 },
    @{ Name = "RagService"; Port = 5003 },
    @{ Name = "WebClient"; Port = 5010 }
)

foreach ($p in $ports) {
    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $tcp.Connect("localhost", $p.Port)
        Write-Host "  $($p.Name) :$($p.Port) OK" -ForegroundColor Green
        $tcp.Close()
    } catch {
        Write-Host "  $($p.Name) :$($p.Port) NOT READY" -ForegroundColor Red
    }
}

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "RagService has ScanOnStartup=true, so knowledge-docs will be re-ingested automatically."
Write-Host "Wait ~30 seconds then check: curl http://localhost:6333/collections/knowledge"
