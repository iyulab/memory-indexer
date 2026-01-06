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
    /// Tags: "tier:recently", "tier:working", "tier:session", "tier:user"
    /// </summary>
    public static IHealthChecksBuilder AddMemoryTierHealthChecks(
        this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<RecentlyBufferHealthCheck>(
                name: "Recently Buffer",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:recently", "memory" })
            .AddCheck<WorkingMemoryHealthCheck>(
                name: "Working Memory",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:working", "memory", "critical" })
            .AddCheck<SessionStoreHealthCheck>(
                name: "Session Store",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "tier", "tier:session", "memory", "critical" })
            .AddCheck<UserProfileHealthCheck>(
                name: "User Profile",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "tier", "tier:user", "memory" });
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
