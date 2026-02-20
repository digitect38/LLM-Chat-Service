# test-chat-exaone4-kr.ps1 - EXAONE 4.0 (1.2B) 한국어 테스트
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)

try {
    Write-Host "=== EXAONE 4.0 (1.2B) Korean Test ===" -ForegroundColor Cyan
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()

    $convId = "test-e4kr-" + [guid]::NewGuid().ToString("N").Substring(0,6)
    $msg = @{
        userMessage    = "CMP 장비에서 슬러리 온도가 비정상적으로 높을 때 어떻게 대처해야 하나요?"
        conversationId = $convId
        modelId        = "ingu627/exaone4.0:1.2b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "Sent: 'CMP 장비에서 슬러리 온도가 비정상적으로 높을 때 어떻게 대처해야 하나요?'" -ForegroundColor Yellow
    Write-Host "(model: ingu627/exaone4.0:1.2b)" -ForegroundColor Yellow
    Write-Host ""

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $firstTokenTime = $null

    while ($ws.State -eq 'Open') {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(60000)

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
                Write-Host "  Chars:       $($fullResponse.Length)" -ForegroundColor White
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
