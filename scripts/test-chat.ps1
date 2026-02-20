# test-chat.ps1 - WebSocket 채팅 테스트
param(
    [string]$Message = "안녕하세요! 간단히 자기소개 해주세요.",
    [string]$Model = "exaone3.5:7.8b",
    [string]$Equipment = "CMP01"
)

$uri = [Uri]"ws://localhost:5000/ws/chat/$Equipment"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None

Write-Host "Connecting to ChatGateway..." -ForegroundColor Cyan
$ws.ConnectAsync($uri, $ct).GetAwaiter().GetResult()
Write-Host "Connected!" -ForegroundColor Green

# Send chat message
$convId = "test-cli-" + [guid]::NewGuid().ToString("N").Substring(0,8)
$msg = @{
    userMessage    = $Message
    conversationId = $convId
    modelId        = $Model
} | ConvertTo-Json -Compress

Write-Host "Sending: $Message" -ForegroundColor Yellow
Write-Host "Model: $Model | ConvID: $convId" -ForegroundColor DarkGray
Write-Host ""

$bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
$segment = New-Object System.ArraySegment[byte]($bytes, 0, $bytes.Length)
$ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()

# Receive streaming response
$fullResponse = ''
$buf = New-Object byte[] 8192
$timeout = [DateTime]::Now.AddSeconds(120)

while ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open -and [DateTime]::Now -lt $timeout) {
    try {
        $seg = New-Object System.ArraySegment[byte]($buf, 0, $buf.Length)
        $cts = New-Object System.Threading.CancellationTokenSource(30000)
        $result = $ws.ReceiveAsync($seg, $cts.Token).GetAwaiter().GetResult()

        if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Text) {
            $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $result.Count)
            $json = $text | ConvertFrom-Json

            if ($json.error) {
                Write-Host ""
                Write-Host "ERROR: $($json.error)" -ForegroundColor Red
                break
            }

            if ($json.token) {
                Write-Host $json.token -NoNewline
                $fullResponse += $json.token
            }

            if ($json.isComplete -eq $true) {
                Write-Host ""
                Write-Host ""
                Write-Host "--- Response Complete ---" -ForegroundColor Green
                break
            }
        }
        elseif ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            Write-Host ""
            Write-Host "Connection closed by server" -ForegroundColor Yellow
            break
        }
    } catch {
        if ([DateTime]::Now -ge $timeout) {
            Write-Host ""
            Write-Host "Timeout waiting for response" -ForegroundColor Red
        } else {
            Write-Host ""
            Write-Host "Error: $_" -ForegroundColor Red
        }
        break
    }
}

$ws.Dispose()
Write-Host "Response length: $($fullResponse.Length) chars" -ForegroundColor Cyan
