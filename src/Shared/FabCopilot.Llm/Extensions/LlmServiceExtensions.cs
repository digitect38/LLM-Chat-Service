using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FabCopilot.Llm.Extensions;

public static class LlmServiceExtensions
{
    public static IServiceCollection AddFabLlm(this IServiceCollection services, IConfiguration configuration)
    {
        // Chat provider options
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<TgiOptions>(
            configuration.GetSection(TgiOptions.SectionName));
        services.Configure<LlmProviderOptions>(
            configuration.GetSection(LlmProviderOptions.SectionName));

        // Embedding provider options
        services.Configure<EmbeddingProviderOptions>(
            configuration.GetSection(EmbeddingProviderOptions.SectionName));
        services.Configure<TeiOptions>(
            configuration.GetSection(TeiOptions.SectionName));

        // Embedding provider selection
        var embeddingProvider = configuration.GetSection("Embedding")?.GetValue<string>("Provider") ?? "Ollama";

        if (embeddingProvider.Equals("Tei", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmbeddingClient, TeiEmbeddingClient>();
        else
            services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();

        // Chat provider selection
        var llmProvider = configuration.GetSection("Llm")?.GetValue<string>("Provider") ?? "Ollama";

        if (llmProvider.Equals("Tgi", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ILlmClient, TgiLlmClient>();
        else
            services.AddSingleton<ILlmClient, OllamaLlmClient>();

        return services;
    }
}
