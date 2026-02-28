using FabCopilot.Messaging.Extensions;
using FabCopilot.Llm.Extensions;
using FabCopilot.VectorStore.Extensions;
using FabCopilot.Observability.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.RagService;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.RagService.Services;
using FabCopilot.RagService.Services.Bm25;
using FabCopilot.RagService.Services.Evaluation;
using FabCopilot.RagService.Services.ImageOcr;
using Microsoft.Extensions.Options;

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

        // BM25 index
        services.AddSingleton<IBm25Index>(sp =>
        {
            var ragOpts = sp.GetRequiredService<IOptions<RagOptions>>().Value;
            return new Bm25Index(ragOpts.Bm25K1, ragOpts.Bm25B);
        });

        // Synonym dictionary
        services.AddSingleton<ISynonymDictionary>(_ =>
        {
            var dictPath = Path.Combine(AppContext.BaseDirectory, "synonym-dictionary.json");
            return SynonymDictionary.LoadFromFile(dictPath);
        });

        // RAG cache
        services.AddSingleton<IRagCache, RedisRagCache>();

        // Query Intelligence Pipeline (3-Stage)
        services.AddSingleton<QueryIntelligencePipeline>();

        // A/B Testing Framework
        services.AddSingleton<AbTestManager>();

        // RAG pipeline services
        services.AddSingleton<IQueryRewriter, LlmQueryRewriter>();
        services.AddSingleton<ILlmReranker, LlmReranker>();
        services.AddSingleton<IKnowledgeGraphStore, RedisKnowledgeGraphStore>();
        services.AddSingleton<IEntityExtractor, LlmEntityExtractor>();
        services.AddSingleton<IAgenticRagOrchestrator, AgenticRagOrchestrator>();

        // Image OCR (optional, requires external OCR service)
        services.AddHttpClient("OCR", (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var baseUrl = config["Ocr:BaseUrl"] ?? "http://localhost:8500";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(config.GetValue("Ocr:TimeoutSeconds", 30));
        });
        services.AddSingleton<IImageOcrExtractor, HttpImageOcrExtractor>();

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
