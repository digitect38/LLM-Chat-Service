using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FabCopilot.Llm.Extensions;

public static class LlmServiceExtensions
{
    public static IServiceCollection AddFabLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));

        services.AddSingleton<ILlmClient, OllamaLlmClient>();

        return services;
    }
}
