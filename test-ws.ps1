$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(60000)

try {
    Write-Host "Connecting to $uri..."
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected! State: $($ws.State)"

    # Send chat message
    $msg = '{"userMessage":"What is 2+2? Reply in one short sentence.","conversationId":"test-001"}'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Sent: $msg"
    Write-Host ""
    Write-Host "--- Receiving response ---"

    # Receive response chunks
    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $timeout = [DateTime]::UtcNow.AddSeconds(45)

    while ($ws.State -eq 'Open' -and [DateTime]::UtcNow -lt $timeout) {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(30000)

        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            Write-Host "`n[Timeout waiting for more data]"
            break
        }

        if ($result.MessageType -eq 'Close') {
            Write-Host "`n[Server closed connection]"
            break
        }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

        # Parse the JSON chunk
        try {
            $json = $text | ConvertFrom-Json
            if ($json.token) {
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                Write-Host "`n`n[Stream complete after $chunkCount chunks]"
                break
            }
        } catch {
            Write-Host "Raw chunk: $text"
        }
    }

    Write-Host "`n--- Full response ---"
    Write-Host $fullResponse
    Write-Host "Total chunks: $chunkCount"

    # Close gracefully
    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Write-Host "WebSocket closed gracefully"
} catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        Write-Host "Inner: $($_.Exception.InnerException.Message)"
    }
} finally {
    $ws.Dispose()
}
