using FabCopilot.Observability.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace FabCopilot.Observability.Extensions;

public static class ObservabilityServiceExtensions
{
    public static IHostBuilder AddFabObservability(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var options = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.SectionName).Bind(options);

        // Configure Serilog
        hostBuilder.UseSerilog((context, loggerConfig) =>
        {
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", options.ServiceName)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(options.LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30);
        });

        return hostBuilder;
    }

    public static IServiceCollection AddFabTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.SectionName).Bind(options);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(options.ServiceName))
            .WithTracing(builder =>
            {
                if (options.EnableTracing)
                {
                    builder
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();
                }
            })
            .WithMetrics(builder =>
            {
                if (options.EnableMetrics)
                {
                    builder
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();
                }
            });

        return services;
    }
}
