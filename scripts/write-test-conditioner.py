import codecs

script = r'''$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)
$outputFile = "D:\__WORK2__\LLM-Chat-Service-master\scripts\test-result-conditioner.txt"

try {
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()

    $convId = "test-cond-" + [guid]::NewGuid().ToString("N").Substring(0,8)
    $msg = @{
        userMessage    = "컨디셔너 디스크 교체 시기는 언제인가요? 구체적인 시간 기준을 알려주세요."
        conversationId = $convId
        modelId        = "exaone3.5:7.8b"
    } | ConvertTo-Json -Compress

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

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
            break
        }

        if ($result.MessageType -eq 'Close') { break }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunkCount++

        try {
            $json = $text | ConvertFrom-Json
            if ($json.token) {
                $fullResponse += $json.token
            }
            if ($json.isComplete -eq $true) {
                break
            }
        } catch {}
    }

    [System.IO.File]::WriteAllText($outputFile, $fullResponse, [System.Text.UTF8Encoding]::new($true))
    Write-Host "Response saved ($chunkCount chunks, $($fullResponse.Length) chars)"

    if ($fullResponse -match "200") {
        Write-Host "PASS: 200 hours found (conditioner disk)"
    } else {
        Write-Host "WARN: 200 hours NOT found"
    }
    if ($fullResponse -match "500") {
        Write-Host "INFO: 500 hours also mentioned (check for confusion with pad)"
    }

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
} catch {
    Write-Host ("ERROR: " + $_.Exception.Message)
} finally {
    $ws.Dispose()
}
'''

with codecs.open(r"D:\__WORK2__\LLM-Chat-Service-master\scripts\test-conditioner-save.ps1", "w", "utf-8-sig") as f:
    f.write(script)

print("Written test-conditioner-save.ps1")
