using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Sdk.Health;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Extension methods for configuring Memory Indexer health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds comprehensive health checks for all Memory Indexer components.
    /// Includes 4-tier memory architecture and infrastructure checks.
    /// </summary>
    /// <param name="builder">The health check builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHealthChecksBuilder AddMemoryIndexerHealthChecks(
        this IHealthChecksBuilder builder)
    {
        return builder
            .AddMemoryTierHealthChecks()
            .AddInfrastructureHealthChecks();
    }

    /// <summary>
    /// Adds health checks for the 4-tier memory architecture.
    /// Tags: "tier:sensory", "tier:working", "tier:episodic", "tier:semantic"
    /// </summary>
    public static IHealthChecksBuilder AddMemoryTierHealthChecks(
        this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<BufferHealthCheck>(
                name: "Buffer",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:sensory", "memory" })
            .AddCheck<ShortTermMemoryHealthCheck>(
                name: "Short-Term Memory",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:working", "memory", "critical" })
            .AddCheck<LongTermStoreHealthCheck>(
                name: "Episodic Store",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:episodic", "memory", "critical" })
            .AddCheck<ArchiveStoreHealthCheck>(
                name: "Semantic Store",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "tier", "tier:semantic", "memory" });
    }

    /// <summary>
    /// Adds health checks for infrastructure components (Vector DB, Embedding service).
    /// Tags: "infrastructure:storage", "infrastructure:embedding"
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(
        this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<VectorDbHealthCheck>(
                name: "Vector Database",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "infrastructure", "infrastructure:storage", "critical" })
            .AddCheck<EmbeddingServiceHealthCheck>(
                name: "Embedding Service",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "infrastructure", "infrastructure:embedding", "critical" });
    }

    /// <summary>
    /// Adds Memory Indexer health checks with service registration.
    /// Use this method in your Program.cs or Startup.cs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMemoryIndexerHealthChecks(
        this IServiceCollection services)
    {
        return services
            .AddHealthChecks()
            .AddMemoryIndexerHealthChecks()
            .Services;
    }
}
