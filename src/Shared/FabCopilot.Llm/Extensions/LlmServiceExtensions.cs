using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FabCopilot.Llm.Extensions;

public static class LlmServiceExtensions
{
    public static IServiceCollection AddFabLlm(this IServiceCollection services, IConfiguration configuration)
    {
        // Chat (always Ollama)
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));

        // Embedding provider selection
        services.Configure<EmbeddingProviderOptions>(
            configuration.GetSection(EmbeddingProviderOptions.SectionName));
        services.Configure<TeiOptions>(
            configuration.GetSection(TeiOptions.SectionName));

        var provider = configuration.GetSection("Embedding")?.GetValue<string>("Provider") ?? "Ollama";

        if (provider.Equals("Tei", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmbeddingClient, TeiEmbeddingClient>();
        else
            services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();

        services.AddSingleton<ILlmClient, OllamaLlmClient>();

        return services;
    }
}
