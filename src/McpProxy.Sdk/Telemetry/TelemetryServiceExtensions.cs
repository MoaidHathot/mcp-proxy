using System.Diagnostics.Metrics;
using McpProxy.Sdk.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace McpProxy.Sdk.Telemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry services.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The proxy configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProxyTelemetry(
        this IServiceCollection services,
        ProxyConfiguration configuration)
    {
        var telemetryConfig = configuration.Proxy.Telemetry;
        
        if (!telemetryConfig.Enabled)
        {
            // Register no-op implementations
            services.AddSingleton<ProxyMetrics>(_ => new ProxyMetrics(new NoOpMeterFactory()));
            services.AddSingleton<ProxyActivitySource>();
            return services;
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: telemetryConfig.ServiceName,
                serviceVersion: telemetryConfig.ServiceVersion ?? "1.0.0");

        // Configure metrics
        if (telemetryConfig.Metrics.Enabled)
        {
            services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(ProxyMetrics.MeterName);

                    if (telemetryConfig.Metrics.ConsoleExporter)
                    {
                        metrics.AddConsoleExporter();
                    }

                    if (!string.IsNullOrEmpty(telemetryConfig.Metrics.OtlpEndpoint))
                    {
                        metrics.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(telemetryConfig.Metrics.OtlpEndpoint);
                        });
                    }
                });
        }

        // Configure tracing
        if (telemetryConfig.Tracing.Enabled)
        {
            services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(ProxyActivitySource.SourceName);

                    if (telemetryConfig.Tracing.ConsoleExporter)
                    {
                        tracing.AddConsoleExporter();
                    }

                    if (!string.IsNullOrEmpty(telemetryConfig.Tracing.OtlpEndpoint))
                    {
                        tracing.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(telemetryConfig.Tracing.OtlpEndpoint);
                        });
                    }
                });
        }

        // Register proxy-specific telemetry services
        services.AddSingleton<ProxyMetrics>();
        services.AddSingleton(sp => new ProxyActivitySource(telemetryConfig.ServiceVersion));

        return services;
    }

    /// <summary>
    /// A no-op meter factory for when telemetry is disabled.
    /// </summary>
    private sealed class NoOpMeterFactory : IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
        {
            return new System.Diagnostics.Metrics.Meter(options);
        }

        public void Dispose()
        {
        }
    }
}
