using FabCopilot.McpLogServer;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.McpLogServer.Services;
using FabCopilot.McpLogServer.Tools;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Observability.Extensions;

Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .AddFabObservability(BuildBootstrapConfiguration())
    .ConfigureServices((ctx, services) =>
    {
        services.AddFabMessaging(ctx.Configuration);
        services.AddFabTelemetry(ctx.Configuration);

        // Register MCP tools
        services.AddSingleton<IMcpTool, SearchLogsTool>();
        services.AddSingleton<IMcpTool, ExtractAlarmWindowTool>();
        services.AddSingleton<IMcpTool, GetTimeSeriesTool>();
        services.AddSingleton<IMcpTool, SummarizeLogsTool>();
        services.AddSingleton<IMcpTool, DetectAnomaliesTool>();

        // Register MCP services
        services.AddSingleton<McpToolDispatcher>();
        services.AddHostedService<McpLogWorker>();
    })
    .Build()
    .Run();

static IConfiguration BuildBootstrapConfiguration()
{
    return new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
}
