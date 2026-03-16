using System.Text.Json;
using System.Text.Json.Nodes;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Redis.Extensions;
using FabCopilot.Redis.Interfaces;
using FabCopilot.Observability.Extensions;
using FabCopilot.ChatGateway.Configuration;
using FabCopilot.ChatGateway.Services;
using FabCopilot.ChatGateway.Services.Engines;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddFabObservability(builder.Configuration);
builder.Services.AddFabMessaging(builder.Configuration);
builder.Services.AddFabRedis(builder.Configuration);
builder.Services.AddFabTelemetry(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
builder.Services.AddHostedService<ChatStreamRelayService>();
builder.Services.AddHttpClient("Whisper", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Whisper:BaseUrl"] ?? "http://localhost:8300";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.GetValue("Whisper:TimeoutSeconds", 60));
});
// TTS Adaptation Layer — 8-engine multi-provider with hot-reload
builder.Services.Configure<TtsOptions>(builder.Configuration.GetSection(TtsOptions.SectionName));
builder.Services.AddSingleton<ITtsEngine, EdgeTtsEngine>();
builder.Services.AddSingleton<ITtsEngine>(sp => new XttsTtsEngine(
    sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<XttsTtsEngine>>()));
builder.Services.AddSingleton<ITtsEngine>(sp => new OpenAiCompatTtsEngine(
    "Kokoro", sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ITtsEngine>(sp => new OpenAiCompatTtsEngine(
    "CosyVoice", sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ITtsEngine>(sp => new OpenAiCompatTtsEngine(
    "Chatterbox", sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ITtsEngine, FishSpeechTtsEngine>();
builder.Services.AddSingleton<ITtsEngine, BarkTtsEngine>();
builder.Services.AddSingleton<ITtsEngine>(sp => new OpenAiCompatTtsEngine(
    "Piper", sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ITtsEngine>(sp => new OpenAiCompatTtsEngine(
    "Orpheus", sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<TtsEngineResolver>();
builder.Services.AddHttpClient("TTS");

var app = builder.Build();

app.UseCors();
app.UseSerilogRequestLogging();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/ws/chat/{equipmentId}", async (HttpContext ctx, string equipmentId, IConnectionManager connMgr) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await connMgr.HandleConnectionAsync(equipmentId, ws, ctx.RequestAborted);
});

// File upload endpoint — uploaded files are saved to the knowledge-docs folder
// for automatic ingestion by the FileWatcherIngestorService
app.MapPost("/api/upload/{equipmentId}", async (HttpRequest req, string equipmentId, IConfiguration config, ILogger<Program> logger) =>
{
    var uploadSection = config.GetSection("Upload");
    var destFolder = uploadSection["DestinationFolder"] ?? "knowledge-docs";
    var maxSizeMb = uploadSection.GetValue<int?>("MaxFileSizeMb") ?? 50;
    var allowedExts = uploadSection.GetSection("AllowedExtensions").Get<string[]>()
                      ?? [".pdf", ".md", ".txt", ".png", ".jpg", ".jpeg"];

    if (!req.HasFormContentType)
        return Results.BadRequest(new { error = "Content-Type must be multipart/form-data" });

    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file provided" });

    if (file.Length > maxSizeMb * 1024L * 1024L)
        return Results.BadRequest(new { error = $"File exceeds {maxSizeMb}MB limit" });

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExts.Contains(ext))
        return Results.BadRequest(new { error = $"File type '{ext}' is not allowed. Allowed: {string.Join(", ", allowedExts)}" });

    // Sanitize filename
    var safeName = Path.GetFileNameWithoutExtension(file.FileName)
        .Replace(' ', '_')
        .Replace("..", "");
    var fileName = $"{safeName}{ext}";

    // Save to destination folder (organized by equipment)
    var fullDestFolder = Path.GetFullPath(destFolder);
    if (!Directory.Exists(fullDestFolder))
        Directory.CreateDirectory(fullDestFolder);

    var destPath = Path.Combine(fullDestFolder, fileName);

    await using var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
    await file.CopyToAsync(stream);

    logger.LogInformation("File uploaded: {FileName} ({Size} bytes) for equipment {EquipmentId} -> {DestPath}",
        file.FileName, file.Length, equipmentId, destPath);

    return Results.Ok(new { fileName, size = file.Length, equipmentId, path = destPath });
}).DisableAntiforgery();

// Whisper STT health check endpoint
app.MapGet("/api/transcribe/health", async (IHttpClientFactory httpFactory) =>
{
    try
    {
        using var client = httpFactory.CreateClient("Whisper");
        client.Timeout = TimeSpan.FromSeconds(3);
        var resp = await client.GetAsync("/health");
        return resp.IsSuccessStatusCode
            ? Results.Ok(new { status = "ok" })
            : Results.StatusCode(503);
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

// Whisper STT transcription proxy endpoint
app.MapPost("/api/transcribe/{equipmentId}", async (
    HttpRequest req, string equipmentId,
    IConfiguration config, IHttpClientFactory httpFactory,
    ILogger<Program> logger) =>
{
    if (!req.HasFormContentType)
        return Results.BadRequest(new { error = "Content-Type must be multipart/form-data" });

    IFormCollection form;
    try { form = await req.ReadFormAsync(); }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Invalid form data: {ex.Message}" });
    }

    var file = form.Files["audio"] ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No audio file provided" });

    var maxSizeMb = config.GetValue("Whisper:MaxFileSizeMb", 25);
    if (file.Length > maxSizeMb * 1024L * 1024L)
        return Results.BadRequest(new { error = $"Audio exceeds {maxSizeMb}MB limit" });

    // Forward to Whisper server (language auto-detect if not specified)
    using var client = httpFactory.CreateClient("Whisper");
    using var content = new MultipartFormDataContent();
    using var audioStream = file.OpenReadStream();
    var streamContent = new StreamContent(audioStream);
    streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
        file.ContentType ?? "audio/webm");
    content.Add(streamContent, "file", file.FileName ?? "audio.webm");
    var language = config["Whisper:Language"];
    if (!string.IsNullOrEmpty(language))
        content.Add(new StringContent(language), "language");

    // Forward domain prompt biasing if provided
    var prompt = form.ContainsKey("prompt") ? form["prompt"].ToString() : null;
    if (!string.IsNullOrEmpty(prompt))
        content.Add(new StringContent(prompt), "prompt");

    HttpResponseMessage response;
    try
    {
        response = await client.PostAsync("/v1/audio/transcriptions", content);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        logger.LogWarning(ex, "Whisper server unreachable");
        return Results.Json(new { error = "Whisper 서버에 연결할 수 없습니다. --profile whisper 로 컨테이너를 시작하세요." },
            statusCode: 503);
    }

    if (!response.IsSuccessStatusCode)
    {
        var err = await response.Content.ReadAsStringAsync();
        logger.LogWarning("Whisper transcription failed: {Status} {Error}", response.StatusCode, err);
        return Results.Json(new { error = $"Whisper 변환 실패 ({(int)response.StatusCode}): {err}" },
            statusCode: (int)response.StatusCode);
    }

    var json = await response.Content.ReadAsStringAsync();
    logger.LogInformation("Transcription for {EquipmentId}: {Length} chars", equipmentId, json.Length);
    return Results.Content(json, "application/json");
}).DisableAntiforgery();

// TTS health check endpoint
app.MapGet("/api/tts/health", (TtsEngineResolver resolver, IOptionsMonitor<TtsOptions> ttsOpts) =>
{
    var engine = resolver.Resolve();
    return Results.Ok(new { status = "ok", engine = engine.Name, provider = ttsOpts.CurrentValue.Provider });
});

// TTS current config endpoint
app.MapGet("/api/tts/config", (TtsEngineResolver resolver, IOptionsMonitor<TtsOptions> ttsOpts) =>
{
    var opts = ttsOpts.CurrentValue;
    return Results.Ok(new
    {
        provider = opts.Provider,
        voice = opts.Voice,
        speed = opts.Speed,
        availableEngines = resolver.AvailableEngines
    });
});

// TTS synthesis endpoint — multi-engine adapter layer
app.MapPost("/api/tts/synthesize", async (HttpRequest req, TtsEngineResolver resolver,
    IOptionsMonitor<TtsOptions> ttsOpts, ILogger<Program> logger) =>
{
    var body = await req.ReadFromJsonAsync<TtsSynthesizeRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Text))
        return Results.BadRequest(new { error = "No text provided" });

    var opts = ttsOpts.CurrentValue;

    // Browser engine — signal client to use SpeechSynthesis API
    if (opts.Provider.Equals("Browser", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { engine = "Browser" });

    var voiceText = body.Text;

    // Strip citation markers for voice output
    voiceText = System.Text.RegularExpressions.Regex.Replace(
        voiceText, @"\[MNL-[^\]]*\]|\[cite-\d+\]|\[출처[^\]]*\]|\*\*|##|#+\s", "",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    // Generate voice summary (first 3 sentences) if requested
    if (body.SummaryOnly)
    {
        var sentences = voiceText.Split(new[] { ". ", ".\n", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        voiceText = string.Join(". ", sentences.Take(3));
        if (!voiceText.EndsWith('.')) voiceText += ".";
    }

    logger.LogInformation("TTS synthesize via {Engine}, voice={Voice}, text={Len} chars",
        resolver.CurrentProvider, opts.Voice, voiceText.Length);

    var synth = await resolver.SynthesizeWithFallbackAsync(
        voiceText, opts.Voice, opts, req.HttpContext.RequestAborted);

    if (!synth.Result.IsSuccess)
    {
        logger.LogWarning("TTS synthesis failed (all engines): {Error}", synth.Result.Error);
        return Results.Json(new { error = synth.Result.Error, engine = synth.EngineName }, statusCode: 503);
    }

    if (synth.FallbackFrom is not null)
        logger.LogWarning("[TTS] Served by FALLBACK engine '{Engine}' voice='{Voice}' (primary '{Primary}' failed). Chain: {Chain}",
            synth.EngineName, synth.VoiceUsed, synth.FallbackFrom, string.Join(" > ", synth.Chain));

    // Expose engine/voice/fallback info to client debug logs
    req.HttpContext.Response.Headers["X-TTS-Engine"] = synth.EngineName;
    req.HttpContext.Response.Headers["X-TTS-Voice"] = synth.VoiceUsed;
    if (synth.FallbackFrom is not null)
        req.HttpContext.Response.Headers["X-TTS-Fallback"] = synth.FallbackFrom;
    req.HttpContext.Response.Headers["X-TTS-Chain"] = string.Join(" > ", synth.Chain);
    return Results.File(synth.Result.AudioData, synth.Result.ContentType, "speech.wav");
});

// ═══ SSE Chat Stream endpoint — React Voice MFE direct communication ═══
// Unlike the WebSocket endpoint, this is a single-request/response pattern:
// POST request → NATS publish → NATS subscribe → SSE stream → complete.
app.MapPost("/api/chat/stream", async (
    HttpContext ctx,
    IMessageBus messageBus,
    IConversationStore conversationStore,
    IAuditTrail auditTrail,
    ILogger<Program> logger) =>
{
    var jsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    ChatRequest? chatRequest;
    try
    {
        chatRequest = await ctx.Request.ReadFromJsonAsync<ChatRequest>(jsonOpts, ctx.RequestAborted);
    }
    catch (JsonException ex)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = $"Invalid JSON: {ex.Message}" });
        return;
    }

    if (chatRequest is null || string.IsNullOrWhiteSpace(chatRequest.UserMessage))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "UserMessage is required" });
        return;
    }

    if (string.IsNullOrWhiteSpace(chatRequest.EquipmentId))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "EquipmentId is required" });
        return;
    }

    // Generate conversation ID if not provided
    if (string.IsNullOrWhiteSpace(chatRequest.ConversationId))
        chatRequest.ConversationId = Guid.NewGuid().ToString();

    var conversationId = chatRequest.ConversationId;
    var equipmentId = chatRequest.EquipmentId;

    logger.LogInformation(
        "SSE chat request from equipment {EquipmentId}, conversation {ConversationId}: {MessagePreview}",
        equipmentId, conversationId,
        chatRequest.UserMessage.Length > 80 ? chatRequest.UserMessage[..80] + "..." : chatRequest.UserMessage);

    // Ensure conversation exists in Redis
    var existing = await conversationStore.GetAsync(conversationId, ctx.RequestAborted);
    if (existing is null)
    {
        var newConversation = new FabCopilot.Contracts.Models.Conversation
        {
            ConversationId = conversationId,
            EquipmentId = equipmentId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        await conversationStore.SaveAsync(newConversation, ctx.RequestAborted);
    }

    // Persist user message
    var userMsg = new FabCopilot.Contracts.Models.ChatMessage
    {
        Role = FabCopilot.Contracts.Enums.MessageRole.User,
        Text = chatRequest.UserMessage,
        Timestamp = DateTimeOffset.UtcNow
    };
    await conversationStore.AppendMessageAsync(conversationId, userMsg, ctx.RequestAborted);

    // Set SSE response headers
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // nginx: disable buffering

    // Subscribe to NATS chat.stream.{conversationId} BEFORE publishing
    var streamSubject = FabCopilot.Contracts.Constants.NatsSubjects.ChatStream(conversationId);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    cts.CancelAfter(TimeSpan.FromMinutes(3)); // 3-minute SSE timeout

    // Publish request to NATS
    var correlationId = Guid.NewGuid().ToString("N");
    var envelope = MessageEnvelope<ChatRequest>.Create(
        type: "chat.request",
        payload: chatRequest,
        equipmentId: equipmentId,
        correlationId: correlationId);

    await messageBus.PublishAsync(FabCopilot.Contracts.Constants.NatsSubjects.ChatRequest, envelope, ctx.RequestAborted);
    _ = auditTrail.LogQueryAsync(equipmentId, conversationId, chatRequest.UserMessage, ctx.RequestAborted);

    logger.LogInformation("SSE: Published chat request, subscribing to {Subject}", streamSubject);

    // Stream SSE events from NATS subscription
    try
    {
        await foreach (var chunkEnvelope in messageBus.SubscribeAsync<ChatStreamChunk>(
            streamSubject, queueGroup: null, ct: cts.Token))
        {
            if (chunkEnvelope.Payload is null) continue;

            var chunk = chunkEnvelope.Payload;
            var json = JsonSerializer.Serialize(chunk, jsonOpts);

            await ctx.Response.WriteAsync($"data: {json}\n\n", cts.Token);
            await ctx.Response.Body.FlushAsync(cts.Token);

            // Persist assistant response when stream completes
            if (chunk.IsComplete)
            {
                if (!string.IsNullOrEmpty(chunk.Token))
                {
                    var assistantMsg = new FabCopilot.Contracts.Models.ChatMessage
                    {
                        Role = FabCopilot.Contracts.Enums.MessageRole.Assistant,
                        Text = chunk.Token,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    _ = conversationStore.AppendMessageAsync(conversationId, assistantMsg, CancellationToken.None);
                }

                logger.LogInformation("SSE: Stream completed for conversation {ConversationId}", conversationId);
                break;
            }

            if (!string.IsNullOrEmpty(chunk.Error))
            {
                logger.LogWarning("SSE: Error chunk for conversation {ConversationId}: {Error}",
                    conversationId, chunk.Error);
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("SSE: Client disconnected or timeout for conversation {ConversationId}", conversationId);
    }

    // Send final SSE event
    try
    {
        await ctx.Response.WriteAsync("event: done\ndata: {}\n\n", CancellationToken.None);
        await ctx.Response.Body.FlushAsync(CancellationToken.None);
    }
    catch { /* client may have disconnected */ }
});

// Feedback endpoint — logs user feedback to the audit trail
app.MapPost("/api/feedback", async (FeedbackMessage feedback, IAuditTrail auditTrail, ILogger<Program> logger) =>
{
    await auditTrail.LogFeedbackAsync(feedback.EquipmentId, feedback.ConversationId, feedback.IsPositive);

    logger.LogInformation("Feedback received: {IsPositive} for conversation {ConversationId}, equipment {EquipmentId}",
        feedback.IsPositive ? "positive" : "negative", feedback.ConversationId, feedback.EquipmentId);

    return Results.Ok(new { status = "recorded" });
});

// PDF serving endpoint — serves documents from knowledge-docs for the citation pane viewer
app.MapGet("/api/documents/{fileName}", (string fileName, IConfiguration config) =>
{
    // Prevent directory traversal attacks
    if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
        return Results.BadRequest(new { error = "Invalid file name" });

    var docsFolder = config.GetValue<string>("Rag:WatchFolder") ?? "knowledge-docs";
    var fullPath = Path.GetFullPath(Path.Combine(docsFolder, fileName));
    var fullDocsFolder = Path.GetFullPath(docsFolder);

    // Verify the resolved path is still within the docs folder
    if (!fullPath.StartsWith(fullDocsFolder, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Invalid file path" });

    if (!File.Exists(fullPath))
        return Results.NotFound(new { error = $"Document '{fileName}' not found" });

    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    var contentType = ext switch
    {
        ".pdf" => "application/pdf",
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    return Results.File(fullPath, contentType, fileName);
});

// Conversation history endpoints — returns stored conversations from Redis
app.MapGet("/api/conversations/{equipmentId}", async (string equipmentId, IConversationStore store) =>
{
    var conversations = await store.GetByEquipmentAsync(equipmentId, limit: 30);
    var summaries = conversations.Select(c =>
    {
        var firstUserMsg = c.Messages.FirstOrDefault(m => m.Role == MessageRole.User)?.Text ?? "새 대화";
        var title = firstUserMsg.Length > 30 ? firstUserMsg[..30] + "..." : firstUserMsg;
        return new
        {
            conversationId = c.ConversationId,
            title,
            lastUpdated = c.LastUpdatedAt,
            messageCount = c.Messages.Count
        };
    });
    return Results.Ok(summaries);
});

app.MapGet("/api/conversations/{equipmentId}/{conversationId}", async (string equipmentId, string conversationId, IConversationStore store) =>
{
    var conversation = await store.GetAsync(conversationId);
    if (conversation is null || !conversation.EquipmentId.Equals(equipmentId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound(new { error = "Conversation not found" });

    return Results.Ok(conversation);
});

app.MapDelete("/api/conversations/{equipmentId}/{conversationId}", async (string equipmentId, string conversationId, IConversationStore store, ILogger<Program> logger) =>
{
    await store.DeleteAsync(conversationId, equipmentId);
    logger.LogInformation("Conversation {ConversationId} deleted for equipment {EquipmentId}", conversationId, equipmentId);
    return Results.Ok(new { status = "deleted" });
});

app.Run();

record TtsSynthesizeRequest(string Text, string? Language = "ko", bool SummaryOnly = false);
