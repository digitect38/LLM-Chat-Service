# test-chat-kr.ps1 - 한국어 채팅 테스트
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)

try {
    Write-Host "Connecting..." -ForegroundColor Cyan
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected!" -ForegroundColor Green

    $convId = "test-kr-" + [guid]::NewGuid().ToString("N").Substring(0,8)
    $msg = @{
        userMessage    = "CMP 장비에서 슬러리 공급이 중단되면 어떤 문제가 발생하나요? 간단히 답변해주세요."
        conversationId = $convId
        modelId        = "exaone3.5:7.8b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Sent Korean question about CMP slurry" -ForegroundColor Yellow
    Write-Host ""

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $chunkCount = 0

    while ($ws.State -eq 'Open') {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(90000)

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
            if ($json.error) { Write-Host "`nERROR: $($json.error)" -ForegroundColor Red }
            if ($json.token) {
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                Write-Host "`n`n[Complete - $chunkCount chunks]" -ForegroundColor Green
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
