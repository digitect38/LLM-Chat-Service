using FabCopilot.Messaging.Extensions;
using FabCopilot.Llm.Extensions;
using FabCopilot.VectorStore.Extensions;
using FabCopilot.Observability.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.RagService;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
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
        services.AddFabRedis(ctx.Configuration);
        services.AddFabTelemetry(ctx.Configuration);
        services.Configure<RagOptions>(ctx.Configuration.GetSection(RagOptions.SectionName));

        // RAG pipeline services
        services.AddSingleton<IQueryRewriter, LlmQueryRewriter>();
        services.AddSingleton<ILlmReranker, LlmReranker>();
        services.AddSingleton<IKnowledgeGraphStore, RedisKnowledgeGraphStore>();
        services.AddSingleton<IEntityExtractor, LlmEntityExtractor>();
        services.AddSingleton<IAgenticRagOrchestrator, AgenticRagOrchestrator>();

        // Document ingestion
        services.AddSingleton<DocumentIngestor>();
        services.AddSingleton<FileTextExtractor>();
        services.AddHostedService<FileWatcherIngestorService>();

        // RAG worker
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
