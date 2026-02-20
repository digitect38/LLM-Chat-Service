# test-chat-32b-simple.ps1 - exaone3.5:32b 간단 성능 테스트
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(600000)

try {
    Write-Host "=== exaone3.5:32b Simple Speed Test ===" -ForegroundColor Cyan
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()

    $convId = "test-32b-spd-" + [guid]::NewGuid().ToString("N").Substring(0,6)
    $msg = @{
        userMessage    = "2+2=? 숫자만 답해."
        conversationId = $convId
        modelId        = "exaone3.5:32b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "Sent: '2+2=? 숫자만 답해.' (model: exaone3.5:32b)" -ForegroundColor Yellow
    Write-Host ""

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $firstTokenTime = $null

    while ($ws.State -eq 'Open') {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(300000)

        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            $sw.Stop()
            Write-Host "`n[Timeout after $([math]::Round($sw.Elapsed.TotalSeconds, 1))s, $chunkCount chunks]" -ForegroundColor Red
            break
        }

        if ($result.MessageType -eq 'Close') { break }
        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

        try {
            $json = $text | ConvertFrom-Json
            if ($json.error) {
                Write-Host "ERROR: $($json.error)" -ForegroundColor Red
                break
            }
            if ($json.token) {
                if ($null -eq $firstTokenTime) { $firstTokenTime = $sw.Elapsed }
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                $sw.Stop()
                Write-Host "`n"
                Write-Host "=== Result ===" -ForegroundColor Cyan
                Write-Host "  First token: $([math]::Round($firstTokenTime.TotalSeconds, 1))s" -ForegroundColor White
                Write-Host "  Total time:  $([math]::Round($sw.Elapsed.TotalSeconds, 1))s" -ForegroundColor White
                Write-Host "  Chunks:      $chunkCount" -ForegroundColor White
                if ($sw.Elapsed.TotalSeconds -gt 0 -and $chunkCount -gt 1) {
                    Write-Host "  Speed:       ~$([math]::Round(($chunkCount - 1) / $sw.Elapsed.TotalSeconds, 2)) tok/s" -ForegroundColor White
                }
                break
            }
        } catch {
            Write-Host "Raw: $text" -ForegroundColor DarkYellow
        }
    }

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    $ws.Dispose()
}
