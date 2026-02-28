using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FabCopilot.Llm.Extensions;

public static class LlmServiceExtensions
{
    public static IServiceCollection AddFabLlm(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options with hot-reload support (IOptionsMonitor)
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<TgiOptions>(
            configuration.GetSection(TgiOptions.SectionName));
        services.Configure<LlmProviderOptions>(
            configuration.GetSection(LlmProviderOptions.SectionName));
        services.Configure<EmbeddingProviderOptions>(
            configuration.GetSection(EmbeddingProviderOptions.SectionName));
        services.Configure<TeiOptions>(
            configuration.GetSection(TeiOptions.SectionName));
        services.Configure<FallbackServerOptions>(
            configuration.GetSection(FallbackServerOptions.SectionName));

        // Register concrete implementations (both always available for hot-swap)
        services.AddSingleton<OllamaEmbeddingClient>();
        services.AddSingleton<TeiEmbeddingClient>();
        services.AddSingleton<OllamaLlmClient>();
        services.AddSingleton<TgiLlmClient>();

        // Embedding resolver (hot-reload capable)
        services.AddSingleton<IEmbeddingClient, EmbeddingClientResolver>();

        // LLM resolver (hot-reload capable)
        services.AddSingleton<LlmClientResolver>();

        // Health checker (background service)
        services.AddSingleton<LlmHealthChecker>();
        services.AddHostedService(sp => sp.GetRequiredService<LlmHealthChecker>());

        // Fallback-aware LLM client wraps the resolver
        services.AddSingleton<ILlmClient>(sp =>
        {
            var resolver = sp.GetRequiredService<LlmClientResolver>();
            var healthChecker = sp.GetRequiredService<LlmHealthChecker>();
            var fallbackOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<FallbackServerOptions>>();
            var embeddingClient = sp.GetRequiredService<IEmbeddingClient>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FallbackLlmClient>>();

            return new FallbackLlmClient(resolver, healthChecker, fallbackOptions, embeddingClient, logger);
        });

        return services;
    }
}
