import codecs

script = r'''# test-pad-replacement.ps1
$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)

try {
    Write-Host "Connecting..." -ForegroundColor Cyan
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    Write-Host "Connected!" -ForegroundColor Green
    Write-Host ""

    $convId = "test-pad-" + [guid]::NewGuid().ToString("N").Substring(0,8)
    $msg = @{
        userMessage    = "패드 교체 시기는 언제인가요?"
        conversationId = $convId
        modelId        = "exaone3.5:7.8b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
    Write-Host "[Q]" -NoNewline -ForegroundColor Yellow
    Write-Host " pad replacement timing question sent"
    Write-Host "---" -ForegroundColor DarkGray

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
            if ($json.error) {
                Write-Host ("`nERROR: " + $json.error) -ForegroundColor Red
            }
            if ($json.token) {
                Write-Host -NoNewline $json.token
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                Write-Host ""
                Write-Host "---" -ForegroundColor DarkGray
                Write-Host "[Complete - $chunkCount chunks]" -ForegroundColor Green
                Write-Host ""
                if ($fullResponse -match "500") {
                    Write-Host "[PASS] 500 hours mentioned (pad)" -ForegroundColor Green
                } else {
                    Write-Host "[WARN] 500 hours NOT mentioned" -ForegroundColor Red
                }
                if ($fullResponse -match "200") {
                    Write-Host "[INFO] 200 hours also mentioned (conditioner disk)" -ForegroundColor Yellow
                }
                break
            }
        } catch {
            Write-Host ("Raw: " + $text) -ForegroundColor DarkYellow
        }
    }

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
} catch {
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
} finally {
    $ws.Dispose()
}
'''

with codecs.open(r"D:\__WORK2__\LLM-Chat-Service-master\scripts\test-pad-replacement.ps1", "w", "utf-8-sig") as f:
    f.write(script)

print("Written test-pad-replacement.ps1 with UTF-8 BOM")
