using FabCopilot.Observability.Configuration;
using FabCopilot.Observability.Enrichers;
using FabCopilot.Observability.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

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
                .Enrich.WithProperty("ServiceName", options.ServiceName);

            // Sensitive data masking enricher (INFO+ level)
            if (options.EnableSensitiveDataMasking)
            {
                loggerConfig.Enrich.With<SensitiveDataMaskingEnricher>();
            }

            // Console sink (human-readable)
            loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}");

            // Text file sink (daily rolling)
            loggerConfig.WriteTo.File(options.LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);

            // JSON file sink (for ELK/Loki ingestion)
            if (options.EnableJsonLog)
            {
                loggerConfig.WriteTo.File(
                    new CompactJsonFormatter(),
                    options.JsonLogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14);
            }
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
                        .AddMeter(FabMetrics.MeterName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();
                }
            });

        return services;
    }
}
