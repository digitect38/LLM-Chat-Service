# test-chat-32b.ps1 - exaone3.5:32b 모델 채팅 성능 테스트
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(300000)  # 5 minute timeout for 32B model

try {
    Write-Host "=== exaone3.5:32b Chat Test ===" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "Connecting..." -ForegroundColor DarkGray
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected!" -ForegroundColor Green

    $convId = "test-32b-" + [guid]::NewGuid().ToString("N").Substring(0,8)
    $msg = @{
        userMessage    = "CMP 장비에서 슬러리 공급이 중단되면 어떤 문제가 발생하나요? 간단히 3가지로 답변해주세요."
        conversationId = $convId
        modelId        = "exaone3.5:32b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "Sent question (model: exaone3.5:32b)" -ForegroundColor Yellow
    Write-Host "Waiting for response..." -ForegroundColor DarkGray
    Write-Host ""

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0
    $firstTokenTime = $null

    while ($ws.State -eq 'Open') {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(180000)  # 3 min per-chunk timeout

        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            Write-Host "`n[Timeout]" -ForegroundColor Red
            break
        }

        if ($result.MessageType -eq 'Close') { break }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

        try {
            $json = $text | ConvertFrom-Json
            if ($json.error) {
                Write-Host "`nERROR: $($json.error)" -ForegroundColor Red
                break
            }
            if ($json.token) {
                if ($null -eq $firstTokenTime) {
                    $firstTokenTime = $sw.Elapsed
                }
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                $sw.Stop()
                Write-Host "`n"
                Write-Host "=== Performance ===" -ForegroundColor Cyan
                Write-Host "  First token:    $([math]::Round($firstTokenTime.TotalSeconds, 1))s" -ForegroundColor White
                Write-Host "  Total time:     $([math]::Round($sw.Elapsed.TotalSeconds, 1))s" -ForegroundColor White
                Write-Host "  Chunks:         $chunkCount" -ForegroundColor White
                Write-Host "  Response chars: $($fullResponse.Length)" -ForegroundColor White
                if ($sw.Elapsed.TotalSeconds -gt 0 -and $chunkCount -gt 1) {
                    $tokPerSec = [math]::Round(($chunkCount - 1) / $sw.Elapsed.TotalSeconds, 1)
                    Write-Host "  Speed:          ~$tokPerSec tokens/s" -ForegroundColor White
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
