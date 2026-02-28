using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace FabCopilot.VectorStore.Extensions;

public static class VectorStoreServiceExtensions
{
    public static IServiceCollection AddFabVectorStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));

        services.AddSingleton<QdrantClient>(sp =>
        {
            var options = configuration.GetSection(QdrantOptions.SectionName).Get<QdrantOptions>() ?? new QdrantOptions();
            return new QdrantClient(options.Host, options.GrpcPort);
        });

        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddSingleton<DualIndexManager>();

        return services;
    }
}
