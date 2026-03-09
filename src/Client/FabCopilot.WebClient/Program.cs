using FabCopilot.WebClient.Configuration;
using FabCopilot.WebClient.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 512 * 1024)
    .AddCircuitOptions(o => o.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.Configure<ModelOptions>(builder.Configuration.GetSection(ModelOptions.SectionName));
builder.Services.Configure<EquipmentOptions>(builder.Configuration.GetSection(EquipmentOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<EmbeddingConfigService>();
builder.Services.AddSingleton<LogReaderService>();
builder.Services.AddSingleton<LogAnalyzerService>();
builder.Services.AddHttpClient("Gateway", client =>
{
    var gatewayUrl = builder.Configuration["Gateway:HttpUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(gatewayUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Prevent aggressive browser caching of CSS/JS — always revalidate with ETag
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});
app.UseRouting();

// TTS proxy — same-origin endpoint so mobile browsers don't need cross-port fetch
// Streams response directly instead of buffering entire audio (fixes ~5s overhead)
app.MapPost("/api/tts/synthesize", async (HttpRequest req, IHttpClientFactory httpFactory, ILogger<Program> logger) =>
{
    var client = httpFactory.CreateClient("Gateway");

    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var bodyBytes = ms.ToArray();
    logger.LogInformation("[TTS Proxy] Forwarding {Len} bytes to Gateway", bodyBytes.Length);

    var content = new ByteArrayContent(bodyBytes);
    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    var resp = await client.PostAsync("/api/tts/synthesize", content);

    if (!resp.IsSuccessStatusCode)
    {
        var errBody = await resp.Content.ReadAsStringAsync();
        logger.LogWarning("[TTS Proxy] Gateway returned {Status}: {Error}", (int)resp.StatusCode, errBody);
        return Results.Json(new { error = errBody, status = (int)resp.StatusCode }, statusCode: (int)resp.StatusCode);
    }

    var respContentType = resp.Content.Headers.ContentType?.ToString() ?? "audio/wav";

    // If response is JSON (Browser engine signal), pass through as-is
    if (respContentType.Contains("application/json"))
    {
        var json = await resp.Content.ReadAsStringAsync();
        return Results.Content(json, "application/json");
    }

    // Forward TTS engine/voice/fallback info headers for client debug logs
    foreach (var hdr in new[] { "X-TTS-Engine", "X-TTS-Voice", "X-TTS-Fallback", "X-TTS-Chain" })
    {
        if (resp.Headers.TryGetValues(hdr, out var vals))
            req.HttpContext.Response.Headers[hdr] = vals.FirstOrDefault() ?? "";
    }

    // Stream audio directly instead of buffering
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, respContentType, "speech.wav");
});

// STT/Transcribe proxy — forward all /api/transcribe/* to ChatGateway
app.Map("/api/transcribe/{**rest}", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var client = httpFactory.CreateClient("Gateway");
    var path = ctx.Request.Path + ctx.Request.QueryString;
    var reqMsg = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), path);
    if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        reqMsg.Content = new StreamContent(ctx.Request.Body);
        if (ctx.Request.ContentType != null)
            reqMsg.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
    }
    var resp = await client.SendAsync(reqMsg);
    ctx.Response.StatusCode = (int)resp.StatusCode;
    ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    await resp.Content.CopyToAsync(ctx.Response.Body);
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
