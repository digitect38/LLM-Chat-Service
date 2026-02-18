using FabCopilot.LlmService;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.Llm.Extensions;
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
        services.AddFabRedis(ctx.Configuration);
        services.AddFabLlm(ctx.Configuration);
        services.AddFabTelemetry(ctx.Configuration);
        services.AddHostedService<LlmWorker>();
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
