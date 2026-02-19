using FabCopilot.Messaging.Extensions;
using FabCopilot.Llm.Extensions;
using FabCopilot.VectorStore.Extensions;
using FabCopilot.Observability.Extensions;
using FabCopilot.RagService;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Services;

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
        services.AddFabLlm(ctx.Configuration);
        services.AddFabVectorStore(ctx.Configuration);
        services.AddFabTelemetry(ctx.Configuration);
        services.Configure<RagOptions>(ctx.Configuration.GetSection(RagOptions.SectionName));
        services.AddSingleton<DocumentIngestor>();
        services.AddSingleton<FileTextExtractor>();
        services.AddHostedService<FileWatcherIngestorService>();
        services.AddHostedService<RagWorker>();
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
