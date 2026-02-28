$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP-001"
$ct = [Threading.CancellationToken]::None
$ws.ConnectAsync($uri, $ct).Wait()
Write-Host "Connected"

$msg = '{"userMessage":"패드 교체 기준이 뭐야?"}'
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$segment = [ArraySegment[byte]]::new($bytes)
$ws.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
Write-Host "Sent - waiting for response..."

$buf = [byte[]]::new(8192)
$fullResponse = ""
$deadline = [DateTime]::Now.AddSeconds(120)
while ([DateTime]::Now -lt $deadline -and $ws.State -eq "Open") {
    try {
        $seg = [ArraySegment[byte]]::new($buf)
        $task = $ws.ReceiveAsync($seg, $ct)
        if ($task.Wait(30000)) {
            if ($task.Result.MessageType -eq "Close") { break }
            $text = [Text.Encoding]::UTF8.GetString($buf, 0, $task.Result.Count)
            try {
                $chunk = $text | ConvertFrom-Json
                if ($chunk.token) {
                    Write-Host -NoNewline $chunk.token
                    $fullResponse += $chunk.token
                }
                if ($chunk.error) { Write-Host "ERROR: $($chunk.error)"; break }
                if ($chunk.isComplete -eq $true) { break }
            } catch {
                Write-Host -NoNewline $text
            }
        }
    } catch { break }
}
Write-Host ""
Write-Host "--- DONE (length: $($fullResponse.Length)) ---"
$ws.Dispose()
