using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.Redis.Interfaces;
using FabCopilot.Observability.Extensions;
using FabCopilot.ChatGateway.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddFabObservability(builder.Configuration);
builder.Services.AddFabMessaging(builder.Configuration);
builder.Services.AddFabRedis(builder.Configuration);
builder.Services.AddFabTelemetry(builder.Configuration);
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
builder.Services.AddHostedService<ChatStreamRelayService>();

var app = builder.Build();

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

app.Run();
