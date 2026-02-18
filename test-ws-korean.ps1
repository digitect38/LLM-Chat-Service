$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(300000)

try {
    Write-Host "Connecting to $uri..."
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected! State: $($ws.State)"

    $msg = '{"userMessage":"CMP 장비에서 슬러리 공급이 중단되면 어떤 문제가 발생하나요? 한국어로 답변해주세요.","conversationId":"test-korean-001"}'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Sent Korean message"
    Write-Host ""
    Write-Host "--- Receiving response (Mixtral) ---"

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $timeout = [DateTime]::UtcNow.AddSeconds(240)

    while ($ws.State -eq 'Open' -and [DateTime]::UtcNow -lt $timeout) {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(120000)

        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            Write-Host "`n[Timeout]"
            break
        }

        if ($result.MessageType -eq 'Close') {
            Write-Host "`n[Server closed]"
            break
        }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

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
            if ($json.error) {
                Write-Host "`nERROR from server: $($json.error)"
                break
            }
        } catch {
            Write-Host "Raw: $text"
        }
    }

    Write-Host "`n--- Full response ---"
    Write-Host $fullResponse
    Write-Host "Total chunks: $chunkCount"

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Write-Host "WebSocket closed"
} catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        Write-Host "Inner: $($_.Exception.InnerException.Message)"
    }
} finally {
    $ws.Dispose()
}
