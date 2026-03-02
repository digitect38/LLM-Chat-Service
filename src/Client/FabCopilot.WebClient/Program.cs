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

app.UseStaticFiles();
app.UseRouting();

// TTS proxy — same-origin endpoint so mobile browsers don't need cross-port fetch
app.MapPost("/api/tts/synthesize", async (HttpRequest req, IHttpClientFactory httpFactory, ILogger<Program> logger) =>
{
    var client = httpFactory.CreateClient("Gateway");

    // Buffer the request body so we can log it on failure
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
    var audio = await resp.Content.ReadAsByteArrayAsync();
    logger.LogInformation("[TTS Proxy] Success, returning {Len} bytes WAV", audio.Length);
    return Results.File(audio, "audio/wav", "speech.wav");
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
