# Generate sample JSON log files (Serilog CLEF format) for Log Analyzer testing
# Usage: powershell -ExecutionPolicy Bypass -File scripts/generate-sample-logs.ps1

$logDir = Join-Path (Join-Path $PSScriptRoot "..") "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

$today = Get-Date
$services = @("gateway", "llmservice", "ragservice", "knowledgeservice", "webclient", "dashboard")

# Realistic message templates per service
$gatewayMessages = @(
    @{ mt = "WebSocket connection opened for equipment {EquipmentId}"; l = $null; extra = @{ EquipmentId = "EQUIP-{0}" } }
    @{ mt = "Chat request received, CorrelationId={CorrelationId}"; l = $null; extra = @{} }
    @{ mt = "Published chat.request to NATS for {EquipmentId}"; l = $null; extra = @{ EquipmentId = "EQUIP-{0}" } }
    @{ mt = "TTS synthesis completed in {ElapsedMs}ms using {Engine}"; l = $null; extra = @{ ElapsedMs = "{1}"; Engine = "Kokoro" } }
    @{ mt = "WebSocket connection closed for equipment {EquipmentId}"; l = $null; extra = @{ EquipmentId = "EQUIP-{0}" } }
    @{ mt = "Failed to connect to TTS engine {Engine}"; l = "Error"; extra = @{ Engine = "Kokoro" } }
    @{ mt = "TTS fallback activated: {Primary} -> {Fallback}"; l = "Warning"; extra = @{ Primary = "Kokoro"; Fallback = "EdgeTts" } }
    @{ mt = "Health check timeout for upstream service"; l = "Warning"; extra = @{} }
)

$llmMessages = @(
    @{ mt = "Processing LLM request, model={Model}"; l = $null; extra = @{ Model = "exaone3.5:7.8b" } }
    @{ mt = "RAG context received: {ChunkCount} chunks, score={TopScore}"; l = $null; extra = @{ ChunkCount = "{0}"; TopScore = "0.{1}" } }
    @{ mt = "LLM streaming response started, tokens={MaxTokens}"; l = $null; extra = @{ MaxTokens = "1536" } }
    @{ mt = "LLM response completed in {ElapsedMs}ms"; l = $null; extra = @{ ElapsedMs = "{1}" } }
    @{ mt = "DLP filter applied: {MaskedCount} patterns masked"; l = $null; extra = @{ MaskedCount = "{0}" } }
    @{ mt = "Ollama connection refused, retrying in {RetryMs}ms"; l = "Error"; extra = @{ RetryMs = "5000" } }
    @{ mt = "Token limit exceeded for request, truncating context"; l = "Warning"; extra = @{} }
    @{ mt = "Fallback to SLM model {Model}"; l = "Warning"; extra = @{ Model = "phi3:mini" } }
)

$ragMessages = @(
    @{ mt = "Embedding query: {QueryLength} chars, model={Model}"; l = $null; extra = @{ QueryLength = "{0}"; Model = "snowflake-arctic-embed2" } }
    @{ mt = "Qdrant search returned {ResultCount} candidates"; l = $null; extra = @{ ResultCount = "{0}" } }
    @{ mt = "BM25 hybrid reranking: {InputCount} -> {OutputCount} results"; l = $null; extra = @{ InputCount = "{0}"; OutputCount = "{1}" } }
    @{ mt = "RAG pipeline completed in {ElapsedMs}ms"; l = $null; extra = @{ ElapsedMs = "{1}" } }
    @{ mt = "Qdrant connection failed: {ErrorMessage}"; l = "Error"; extra = @{ ErrorMessage = "Connection refused" } }
    @{ mt = "No relevant documents found for query, score below threshold"; l = "Warning"; extra = @{} }
    @{ mt = "Embedding dimension mismatch: expected {Expected}, got {Actual}"; l = "Error"; extra = @{ Expected = "1024"; Actual = "768" } }
)

$knowledgeMessages = @(
    @{ mt = "Document ingested: {FileName}, {ChunkCount} chunks created"; l = $null; extra = @{ FileName = "manual_{0}.pdf"; ChunkCount = "{1}" } }
    @{ mt = "Chunking completed: structure={Tier1}, semantic={Tier2}, sliding={Tier3}"; l = $null; extra = @{ Tier1 = "{0}"; Tier2 = "{1}"; Tier3 = "0" } }
    @{ mt = "OCR extraction completed for {PageCount} pages"; l = $null; extra = @{ PageCount = "{0}" } }
    @{ mt = "Failed to parse document: {FileName}"; l = "Error"; extra = @{ FileName = "corrupted.pdf" } }
    @{ mt = "Duplicate document detected, skipping: {FileName}"; l = "Warning"; extra = @{ FileName = "manual_{0}.pdf" } }
)

$webclientMessages = @(
    @{ mt = "Page loaded: {PageName}"; l = $null; extra = @{ PageName = "Index" } }
    @{ mt = "SignalR hub connected"; l = $null; extra = @{} }
    @{ mt = "Chat message sent for equipment {EquipmentId}"; l = $null; extra = @{ EquipmentId = "EQUIP-{0}" } }
    @{ mt = "Configuration updated: {Section}"; l = $null; extra = @{ Section = "Embedding" } }
    @{ mt = "SignalR reconnection attempt {Attempt}"; l = "Warning"; extra = @{ Attempt = "{0}" } }
)

$dashboardMessages = @(
    @{ mt = "Health check cycle completed: {UpCount} up, {DownCount} down"; l = $null; extra = @{ UpCount = "{0}"; DownCount = "{1}" } }
    @{ mt = "Docker status refreshed: {ContainerCount} containers"; l = $null; extra = @{ ContainerCount = "6" } }
    @{ mt = "Service {ServiceName} state changed: {OldState} -> {NewState}"; l = "Warning"; extra = @{ ServiceName = "Ollama"; OldState = "Up"; NewState = "Down" } }
)

$messageMap = @{
    "gateway" = $gatewayMessages
    "llmservice" = $llmMessages
    "ragservice" = $ragMessages
    "knowledgeservice" = $knowledgeMessages
    "webclient" = $webclientMessages
    "dashboard" = $dashboardMessages
}

$serviceDisplayNames = @{
    "gateway" = "ChatGateway"
    "llmservice" = "LlmService"
    "ragservice" = "RagService"
    "knowledgeservice" = "KnowledgeService"
    "webclient" = "WebClient"
    "dashboard" = "ServiceDashboard"
}

$errorExceptions = @(
    "System.Net.Http.HttpRequestException: Connection refused (127.0.0.1:11434)`n   at System.Net.Http.HttpClient.SendAsync()"
    "System.TimeoutException: The operation has timed out.`n   at Grpc.Net.Client.Internal.GrpcCall.RunCall()"
    "System.Text.Json.JsonException: The JSON value could not be converted`n   at System.Text.Json.JsonSerializer.Deserialize()"
    "System.IO.IOException: The process cannot access the file`n   at System.IO.FileStream.WriteCore()"
    "NATS.Client.Core.NatsException: Connection lost`n   at NATS.Client.Core.NatsConnection.PublishAsync()"
)

$random = New-Object System.Random(42)

# Generate logs for today and yesterday
foreach ($dayOffset in @(0, 1)) {
    $date = $today.AddDays(-$dayOffset)
    $dateStr = $date.ToString("yyyyMMdd")

    foreach ($service in $services) {
        $fileName = "$service-$dateStr.json"
        $filePath = Join-Path $logDir $fileName
        $lines = @()
        $messages = $messageMap[$service]
        $displayName = $serviceDisplayNames[$service]

        # Generate 50-200 log entries per service per day
        $entryCount = $random.Next(50, 201)

        # Generate some correlation IDs to reuse across services
        $correlationIds = @()
        for ($i = 0; $i -lt 15; $i++) {
            $correlationIds += [guid]::NewGuid().ToString().Substring(0, 8)
        }

        for ($i = 0; $i -lt $entryCount; $i++) {
            $msg = $messages[$random.Next($messages.Count)]
            $hour = $random.Next(0, 24)
            $minute = $random.Next(0, 60)
            $second = $random.Next(0, 60)
            $ms = $random.Next(0, 1000)
            $timestamp = $date.Date.AddHours($hour).AddMinutes($minute).AddSeconds($second).AddMilliseconds($ms)
            $isoTime = $timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")

            $cid = $correlationIds[$random.Next($correlationIds.Count)]

            # Build JSON object
            $obj = [ordered]@{
                "@t" = $isoTime
                "@mt" = $msg.mt
                "ServiceName" = $displayName
                "CorrelationId" = $cid
            }

            if ($msg.l) {
                $obj["@l"] = $msg.l
            }

            # Add extra properties
            foreach ($key in $msg.extra.Keys) {
                $val = $msg.extra[$key]
                $val = $val -replace "\{0\}", $random.Next(1, 50)
                $val = $val -replace "\{1\}", $random.Next(10, 3000)
                $obj[$key] = $val
            }

            # Add exception for error entries
            if ($msg.l -eq "Error" -and $random.Next(100) -lt 60) {
                $obj["@x"] = $errorExceptions[$random.Next($errorExceptions.Count)]
            }

            $json = $obj | ConvertTo-Json -Compress
            $lines += $json
        }

        # Sort by timestamp
        $lines = $lines | Sort-Object

        # Write file
        $lines | Out-File -FilePath $filePath -Encoding utf8
        Write-Host "Generated: $fileName ($($lines.Count) entries)"
    }
}

# Also generate some cross-service correlated flows (shared CIDs across gateway -> llm -> rag)
$sharedCids = @()
for ($i = 0; $i -lt 5; $i++) {
    $sharedCids += "flow-" + [guid]::NewGuid().ToString().Substring(0, 6)
}

$dateStr = $today.ToString("yyyyMMdd")

foreach ($cid in $sharedCids) {
    $baseHour = $random.Next(8, 22)
    $baseMinute = $random.Next(0, 60)
    $baseSecond = $random.Next(0, 60)
    $baseTime = $today.Date.AddHours($baseHour).AddMinutes($baseMinute).AddSeconds($baseSecond)

    # Gateway receives request
    $t1 = $baseTime
    $obj1 = [ordered]@{
        "@t" = $t1.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "Chat request received, CorrelationId={CorrelationId}"
        "ServiceName" = "ChatGateway"
        "CorrelationId" = $cid
    }
    ($obj1 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "gateway-$dateStr.json")

    # Gateway publishes to NATS
    $t2 = $t1.AddMilliseconds($random.Next(5, 30))
    $obj2 = [ordered]@{
        "@t" = $t2.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "Published chat.request to NATS for {EquipmentId}"
        "ServiceName" = "ChatGateway"
        "CorrelationId" = $cid
        "EquipmentId" = "EQUIP-001"
    }
    ($obj2 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "gateway-$dateStr.json")

    # LlmService receives
    $t3 = $t2.AddMilliseconds($random.Next(10, 50))
    $obj3 = [ordered]@{
        "@t" = $t3.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "Processing LLM request, model={Model}"
        "ServiceName" = "LlmService"
        "CorrelationId" = $cid
        "Model" = "exaone3.5:7.8b"
    }
    ($obj3 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "llmservice-$dateStr.json")

    # LlmService sends RAG request
    $t4 = $t3.AddMilliseconds($random.Next(5, 20))
    $obj4 = [ordered]@{
        "@t" = $t4.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "RAG context requested for query"
        "ServiceName" = "LlmService"
        "CorrelationId" = $cid
    }
    ($obj4 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "llmservice-$dateStr.json")

    # RagService processes
    $t5 = $t4.AddMilliseconds($random.Next(20, 100))
    $obj5 = [ordered]@{
        "@t" = $t5.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "Embedding query: {QueryLength} chars, model={Model}"
        "ServiceName" = "RagService"
        "CorrelationId" = $cid
        "QueryLength" = "$($random.Next(20, 200))"
        "Model" = "snowflake-arctic-embed2"
    }
    ($obj5 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "ragservice-$dateStr.json")

    # RagService returns
    $t6 = $t5.AddMilliseconds($random.Next(50, 300))
    $obj6 = [ordered]@{
        "@t" = $t6.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "RAG pipeline completed in {ElapsedMs}ms"
        "ServiceName" = "RagService"
        "CorrelationId" = $cid
        "ElapsedMs" = "$($random.Next(80, 500))"
    }
    ($obj6 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "ragservice-$dateStr.json")

    # LlmService completes
    $t7 = $t6.AddMilliseconds($random.Next(500, 3000))
    $obj7 = [ordered]@{
        "@t" = $t7.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "LLM response completed in {ElapsedMs}ms"
        "ServiceName" = "LlmService"
        "CorrelationId" = $cid
        "ElapsedMs" = "$($random.Next(500, 5000))"
    }
    ($obj7 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "llmservice-$dateStr.json")

    # Gateway sends response
    $t8 = $t7.AddMilliseconds($random.Next(5, 30))
    $obj8 = [ordered]@{
        "@t" = $t8.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        "@mt" = "WebSocket response streamed to client for {EquipmentId}"
        "ServiceName" = "ChatGateway"
        "CorrelationId" = $cid
        "EquipmentId" = "EQUIP-001"
    }
    ($obj8 | ConvertTo-Json -Compress) | Out-File -Append -Encoding utf8 (Join-Path $logDir "gateway-$dateStr.json")
}

Write-Host ""
Write-Host "Sample log generation complete!"
Write-Host "Cross-service CorrelationIds for flow testing:"
foreach ($cid in $sharedCids) {
    Write-Host "  $cid"
}
