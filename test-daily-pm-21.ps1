$uri = "ws://localhost:5000/ws/chat/CMP01"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(180000)

try {
    $ws.ConnectAsync([Uri]$uri, $cts.Token).Wait()
    Write-Host "Connected to $uri"

    $msg = '{"conversationId":"' + [guid]::NewGuid().ToString("N") + '","equipmentId":"CMP01","userMessage":"CMP Daily PM (일일 점검) 2.1 점검항목 7가지를 표 형식으로 상세히 알려주세요. 각 항목의 기준과 조치 사항을 포함해주세요.","modelId":"exaone3.5:7.8b","searchMode":"hybrid"}'

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
                    if ($c.chunkText) {
                        $preview = $c.chunkText
                        if ($preview.Length -gt 120) { $preview = $preview.Substring(0, 120) + "..." }
                        Write-Host "    ChunkText: $preview"
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
                Write-Host "=== NO [Page XX] pattern found ==="
            }

            # Verify 7 inspection items
            Write-Host ""
            Write-Host "=== VERIFICATION: 2.1 Daily PM Items ==="
            $items = @("슬러리 잔량", "DI water", "Vacuum", "패드 상태", "컨디셔너", "슬러리 누수", "알람 이력")
            foreach ($item in $items) {
                if ($fullResponse -match $item) {
                    Write-Host "  [PASS] $item - found in response"
                } else {
                    Write-Host "  [MISS] $item - NOT found in response"
                }
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
