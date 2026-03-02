using FabCopilot.Contracts.Interfaces;
using FabCopilot.Redis.Configuration;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FabCopilot.Redis.Extensions;

public static class RedisServiceExtensions
{
    public static IServiceCollection AddFabRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
            var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.DefaultDatabase = options.DefaultDatabase;
            return ConnectionMultiplexer.Connect(configOptions);
        });

        services.AddSingleton<IConversationStore, RedisConversationStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        services.AddSingleton<IAuditTrail, RedisAuditTrail>();
        services.AddSingleton<IEquipmentRegistry, RedisEquipmentRegistry>();

        return services;
    }
}
