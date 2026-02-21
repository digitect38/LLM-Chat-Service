[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Console]::InputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]'ws://localhost:5000/ws/chat/CMP-001'
$ct = [Threading.CancellationToken]::None
$ws.ConnectAsync($uri, $ct).Wait()
Write-Host "Connected"

$msg = '{"userMessage":"패드 교체 기준이 뭐야?"}'
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$segment = [ArraySegment[byte]]::new($bytes)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$ws.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
Write-Host "Sent - waiting..."

$buf = [byte[]]::new(8192)
$fullResponse = ""
$firstToken = $true
$deadline = [DateTime]::Now.AddSeconds(120)
while ([DateTime]::Now -lt $deadline -and $ws.State -eq "Open") {
    try {
        $seg = [ArraySegment[byte]]::new($buf)
        $task = $ws.ReceiveAsync($seg, $ct)
        $timeout = if ($firstToken) { 60000 } else { 30000 }
        if ($task.Wait($timeout)) {
            if ($firstToken) { Write-Host "[First token: $($sw.ElapsedMilliseconds)ms]"; $firstToken = $false }
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
        } else {
            Write-Host "[timeout]"
            break
        }
    } catch { break }
}
$sw.Stop()
Write-Host "`n--- DONE: $($fullResponse.Length) chars in $($sw.ElapsedMilliseconds)ms ---"
$ws.Dispose()
