$uri = [Uri]"ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(300000)

try {
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()

    $msg = '{"userMessage":"CMP 공정에서 Material Removal Rate 공식을 수학 수식으로 설명해주세요. Preston equation 포함해서 LaTeX 수식으로 보여주세요.","conversationId":"test-latex-001"}'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = New-Object System.ArraySegment[byte](,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

    $buf = New-Object byte[] 8192
    $fullResponse = ""
    $timeout = [DateTime]::UtcNow.AddSeconds(240)

    while ($ws.State -eq 'Open' -and [DateTime]::UtcNow -lt $timeout) {
        $seg = New-Object System.ArraySegment[byte](,$buf)
        $recvCts = New-Object System.Threading.CancellationTokenSource
        $recvCts.CancelAfter(120000)
        try {
            $result = $ws.ReceiveAsync($seg, $recvCts.Token).GetAwaiter().GetResult()
        } catch { break }
        if ($result.MessageType -eq 'Close') { break }
        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        try {
            $json = $text | ConvertFrom-Json
            if ($json.token) { $fullResponse += $json.token }
            if ($json.isComplete -eq $true) { break }
            if ($json.error) { Write-Host "ERROR: $($json.error)"; break }
        } catch {}
    }

    [System.IO.File]::WriteAllText("C:\Develop25\LLM-Chat-Service\test-latex-output.txt", $fullResponse, [System.Text.Encoding]::UTF8)
    Write-Host "Response saved. Length: $($fullResponse.Length)"

    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
} catch {
    Write-Host "ERROR: $($_.Exception.Message)"
} finally {
    $ws.Dispose()
}
