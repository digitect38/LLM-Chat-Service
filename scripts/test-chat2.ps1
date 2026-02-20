# test-chat2.ps1 - WebSocket 채팅 테스트 (디버그 모드)
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)  # 2 minute timeout

try {
    Write-Host "Connecting to $uri..." -ForegroundColor Cyan
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected! State: $($ws.State)" -ForegroundColor Green

    # Send chat message with unique conversationId
    $convId = "test-" + [guid]::NewGuid().ToString("N").Substring(0,8)
    $msg = @{
        userMessage    = "2+2 is? Reply in one sentence."
        conversationId = $convId
        modelId        = "exaone3.5:7.8b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Sent: $msg" -ForegroundColor Yellow
    Write-Host "Waiting for response (up to 2 min)..." -ForegroundColor DarkGray
    Write-Host ""

    # Receive response chunks
    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $timeout = [DateTime]::UtcNow.AddSeconds(120)

    while ($ws.State -eq 'Open' -and [DateTime]::UtcNow -lt $timeout) {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(60000)  # 60 second per-chunk timeout

        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            Write-Host "`n[Timeout waiting for data after $chunkCount chunks]" -ForegroundColor Red
            break
        }

        if ($result.MessageType -eq 'Close') {
            Write-Host "`n[Server closed connection]" -ForegroundColor Yellow
            break
        }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

        try {
            $json = $text | ConvertFrom-Json
            if ($json.error) {
                Write-Host "`nERROR from server: $($json.error)" -ForegroundColor Red
            }
            if ($json.token) {
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                Write-Host "`n`n[Stream complete after $chunkCount chunks]" -ForegroundColor Green
                break
            }
        } catch {
            Write-Host "Raw chunk #$chunkCount : $text" -ForegroundColor DarkYellow
        }
    }

    Write-Host ""
    Write-Host "--- Result ---" -ForegroundColor Cyan
    Write-Host "Response: $fullResponse"
    Write-Host "Chunks: $chunkCount"

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "Inner: $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
} finally {
    $ws.Dispose()
}
