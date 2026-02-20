using FabCopilot.Messaging.Extensions;
using FabCopilot.Redis.Extensions;
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

app.Run();
