$uri = "ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(120000)

try {
    $ws.ConnectAsync([Uri]$uri, $cts.Token).Wait()
    Write-Host "Connected to $uri"

    $msg = '{"conversationId":"' + [guid]::NewGuid().ToString("N") + '","equipmentId":"CMP01","userMessage":"CMP Daily PM (일일 점검) 절차와 점검 항목을 알려주세요","modelId":"exaone3.5:7.8b","searchMode":"hybrid"}'

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
    $segment = [ArraySegment[byte]]::new($bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).Wait()
    Write-Host "Sent query"
    Write-Host "---"

    $buf = New-Object byte[] 8192
    $fullResponse = ""

    while ($ws.State -eq 'Open' -and -not $cts.IsCancellationRequested) {
        $seg = [ArraySegment[byte]]::new($buf)
        $result = $ws.ReceiveAsync($seg, $cts.Token).Result

        if ($result.MessageType -eq 'Close') { break }

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
        $chunk = $text | ConvertFrom-Json

        if ($chunk.token) {
            $fullResponse += $chunk.token
            Write-Host -NoNewline $chunk.token
        }

        if ($chunk.isComplete) {
            Write-Host ""
            Write-Host "=== COMPLETE ==="

            if ($chunk.citations) {
                Write-Host ""
                Write-Host "=== CITATIONS ($($chunk.citations.Count)) ==="
                foreach ($c in $chunk.citations) {
                    $line = "  [$($c.citationId)] $($c.fileName)"
                    $line += " | Page=$($c.page)"
                    $line += " | Score=$($c.score)"
                    $line += " | HighlightType=$($c.highlightType)"
                    $line += " | Revision=$($c.revision)"
                    $line += " | CharOffset=$($c.charOffsetStart)-$($c.charOffsetEnd)"
                    Write-Host $line
                    if ($c.parentContext) {
                        Write-Host "    ParentContext: $($c.parentContext)"
                    }
                }
            }

            $pageMatches = [regex]::Matches($fullResponse, '\[Page \d+(?:\s*,\s*\d+)*\]')
            if ($pageMatches.Count -gt 0) {
                Write-Host ""
                Write-Host "=== [Page XX] CITATIONS FOUND ==="
                foreach ($m in $pageMatches) {
                    Write-Host "  $($m.Value)"
                }
            } else {
                Write-Host ""
                Write-Host "=== NO [Page XX] pattern (tinyllama may not follow format) ==="
            }

            break
        }
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
} finally {
    if ($ws.State -eq 'Open') {
        $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", [System.Threading.CancellationToken]::None).Wait()
    }
    $ws.Dispose()
}
