using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// Extension methods for configuring OpenTelemetry with Memory Indexer.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry observability for Memory Indexer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMemoryIndexerObservability(
        this IServiceCollection services,
        Action<ObservabilityOptions>? configure = null)
    {
        var options = new ObservabilityOptions();
        configure?.Invoke(options);

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName ?? MemoryIndexerTelemetry.ServiceName,
                serviceVersion: options.ServiceVersion ?? MemoryIndexerTelemetry.ServiceVersion,
                serviceInstanceId: options.ServiceInstanceId ?? Environment.MachineName);

        // Add custom resource attributes
        if (options.ResourceAttributes.Count > 0)
        {
            resourceBuilder.AddAttributes(options.ResourceAttributes);
        }

        // Configure tracing
        if (options.EnableTracing)
        {
            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(MemoryIndexerTelemetry.ServiceName)
                        .AddHttpClientInstrumentation();

                    if (options.EnableConsoleExporter)
                    {
                        builder.AddConsoleExporter();
                    }

                    if (!string.IsNullOrEmpty(options.OtlpEndpoint))
                    {
                        builder.AddOtlpExporter(otlpOptions =>
                        {
                            otlpOptions.Endpoint = new Uri(options.OtlpEndpoint);
                            otlpOptions.Protocol = options.OtlpProtocol;
                        });
                    }

                    options.ConfigureTracing?.Invoke(builder);
                });
        }

        // Configure metrics
        if (options.EnableMetrics)
        {
            services.AddOpenTelemetry()
                .WithMetrics(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(MemoryIndexerTelemetry.ServiceName)
                        .AddHttpClientInstrumentation();

                    if (options.EnableConsoleExporter)
                    {
                        builder.AddConsoleExporter();
                    }

                    if (!string.IsNullOrEmpty(options.OtlpEndpoint))
                    {
                        builder.AddOtlpExporter(otlpOptions =>
                        {
                            otlpOptions.Endpoint = new Uri(options.OtlpEndpoint);
                            otlpOptions.Protocol = options.OtlpProtocol;
                        });
                    }

                    options.ConfigureMetrics?.Invoke(builder);
                });
        }

        return services;
    }

    /// <summary>
    /// Adds console-only observability for development/debugging.
    /// </summary>
    public static IServiceCollection AddMemoryIndexerConsoleObservability(this IServiceCollection services)
    {
        return services.AddMemoryIndexerObservability(options =>
        {
            options.EnableConsoleExporter = true;
            options.EnableTracing = true;
            options.EnableMetrics = true;
        });
    }

    /// <summary>
    /// Adds OTLP observability for production use with collectors like Jaeger, Grafana, etc.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="otlpEndpoint">The OTLP endpoint URL (e.g., "http://localhost:4317").</param>
    public static IServiceCollection AddMemoryIndexerOtlpObservability(
        this IServiceCollection services,
        string otlpEndpoint)
    {
        return services.AddMemoryIndexerObservability(options =>
        {
            options.OtlpEndpoint = otlpEndpoint;
            options.EnableTracing = true;
            options.EnableMetrics = true;
        });
    }
}

/// <summary>
/// Configuration options for Memory Indexer observability.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// Service name for telemetry. Defaults to "MemoryIndexer".
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Service version for telemetry. Defaults to assembly version.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Service instance ID. Defaults to machine name.
    /// </summary>
    public string? ServiceInstanceId { get; set; }

    /// <summary>
    /// Whether to enable distributed tracing. Default: true.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Whether to enable metrics collection. Default: true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Whether to export telemetry to console (for debugging). Default: false.
    /// </summary>
    public bool EnableConsoleExporter { get; set; }

    /// <summary>
    /// OTLP endpoint URL for exporting telemetry (e.g., "http://localhost:4317").
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// OTLP export protocol. Default: Grpc.
    /// </summary>
    public OtlpExportProtocol OtlpProtocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>
    /// Additional resource attributes for telemetry.
    /// </summary>
    public Dictionary<string, object> ResourceAttributes { get; set; } = new();

    /// <summary>
    /// Custom configuration action for tracing.
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// Custom configuration action for metrics.
    /// </summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }
}
