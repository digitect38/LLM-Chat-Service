# Restart RagService only, then delete + re-ingest
Write-Host "=== Stop RagService ===" -ForegroundColor Yellow
$proc = Get-Process -Name "FabCopilot.RagService" -ErrorAction SilentlyContinue
if ($proc) {
    Stop-Process -Name "FabCopilot.RagService" -Force
    Write-Host "  Stopped (PID $($proc.Id))" -ForegroundColor Red
}
Start-Sleep -Seconds 2

Write-Host "=== Delete Qdrant collection ===" -ForegroundColor Yellow
try {
    $resp = Invoke-RestMethod -Uri "http://localhost:6333/collections/knowledge" -Method Delete -TimeoutSec 5
    Write-Host "  Deleted: $($resp.status)" -ForegroundColor Green
} catch {
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
}
Start-Sleep -Seconds 1

Write-Host "=== Build ===" -ForegroundColor Yellow
$root = "D:\__WORK2__\LLM-Chat-Service-master"
& dotnet build "$root\src\Services\FabCopilot.RagService\FabCopilot.RagService.csproj" --verbosity quiet 2>&1 | Out-Null
Write-Host "  Build done" -ForegroundColor Green

Write-Host "=== Start RagService ===" -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-Command", "dotnet run --project `"$root\src\Services\FabCopilot.RagService`" --no-build" -WindowStyle Minimized
Write-Host "  Started" -ForegroundColor Green

Write-Host "=== Waiting for ingestion (30s) ===" -ForegroundColor Yellow
Start-Sleep -Seconds 30

try {
    $coll = Invoke-RestMethod -Uri "http://localhost:6333/collections/knowledge" -Method Get -TimeoutSec 5
    $points = $coll.result.points_count
    Write-Host "  Qdrant points: $points" -ForegroundColor Cyan
} catch {
    Write-Host "  Collection not ready yet" -ForegroundColor Yellow
}

Write-Host "=== Done ===" -ForegroundColor Green
