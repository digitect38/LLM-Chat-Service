using FabCopilot.Messaging.Configuration;
using FabCopilot.Messaging.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;

namespace FabCopilot.Messaging.Extensions;

public static class MessagingServiceExtensions
{
    public static IServiceCollection AddFabMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var natsOptions = new NatsOptions();
        configuration.GetSection(NatsOptions.SectionName).Bind(natsOptions);
        services.Configure<NatsOptions>(configuration.GetSection(NatsOptions.SectionName));

        services.AddSingleton(_ =>
        {
            var opts = new NatsOpts
            {
                Url = natsOptions.Url,
                MaxReconnectRetry = natsOptions.MaxReconnectRetries,
                ReconnectWaitMin = TimeSpan.FromMilliseconds(natsOptions.ReconnectWaitMs)
            };

            return new NatsConnection(opts);
        });

        services.AddSingleton<IMessageBus, NatsMessageBus>();

        return services;
    }
}
