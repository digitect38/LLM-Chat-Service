[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Console]::InputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]'ws://localhost:5000/ws/chat/CMP-001'
$ct = [Threading.CancellationToken]::None
$ws.ConnectAsync($uri, $ct).Wait()
Write-Host "Connected to WebSocket"

$msg = '{"userMessage":"CMP 패드 교체 시기 기준이 뭐야?"}'
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$segment = [ArraySegment[byte]]::new($bytes)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$ws.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
Write-Host "Sent query - waiting for response..."

$buf = [byte[]]::new(8192)
$fullResponse = ""
$firstToken = $true
$deadline = [DateTime]::Now.AddSeconds(300)
while ([DateTime]::Now -lt $deadline -and $ws.State -eq "Open") {
    $seg = [ArraySegment[byte]]::new($buf)
    $task = $ws.ReceiveAsync($seg, $ct)
    $timeout = if ($firstToken) { 120000 } else { 60000 }
    if ($task.Wait($timeout)) {
        if ($firstToken) { Write-Host "[First token: $($sw.ElapsedMilliseconds)ms]"; $firstToken = $false }
        if ($task.Result.MessageType -eq "Close") { break }
        $text = [Text.Encoding]::UTF8.GetString($buf, 0, $task.Result.Count)
        $json = $text | ConvertFrom-Json
        if ($json.isComplete) { Write-Host "`n[Complete in $($sw.ElapsedMilliseconds)ms]"; break }
        if ($json.error) { Write-Host "ERROR: $($json.error)"; break }
        Write-Host -NoNewline $json.token
        $fullResponse += $json.token
    } else {
        Write-Host "Timeout waiting for token"; break
    }
}
$ws.Dispose()
Write-Host "`n--- Response length: $($fullResponse.Length) chars ---"
