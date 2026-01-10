using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using MemoryIndexer.Sdk.Health;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Extension methods for mapping Memory Indexer health check endpoints.
/// Provides Kubernetes-compatible probe endpoints.
/// </summary>
public static class HealthEndpointExtensions
{
    /// <summary>
    /// Maps all Memory Indexer health check endpoints with Kubernetes-compatible probes.
    /// Endpoints: /health, /health/live, /health/ready, /health/startup
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pathPrefix">Path prefix for health endpoints. Default is "/health".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapMemoryIndexerHealthChecks(
        this IEndpointRouteBuilder endpoints,
        string pathPrefix = "/health")
    {
        // Comprehensive health check (all checks)
        endpoints.MapHealthChecks(pathPrefix, new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        // Liveness probe: Is the app running?
        // Returns healthy if the app process is alive (no actual checks)
        endpoints.MapHealthChecks($"{pathPrefix}/live", new HealthCheckOptions
        {
            Predicate = _ => false, // No checks, just confirms process is alive
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        // Readiness probe: Is the app ready to accept traffic?
        // Checks critical infrastructure components
        endpoints.MapHealthChecks($"{pathPrefix}/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("critical"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        // Startup probe: Has the app finished starting up?
        // Checks all memory tiers are initialized
        endpoints.MapHealthChecks($"{pathPrefix}/startup", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("tier"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }

    /// <summary>
    /// Maps a liveness probe endpoint.
    /// Kubernetes uses this to determine if the container should be restarted.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default is "/health/live".</param>
    public static IEndpointRouteBuilder MapLivenessProbe(
        this IEndpointRouteBuilder endpoints,
        string path = "/health/live")
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = _ => false, // Always healthy if process is running
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }

    /// <summary>
    /// Maps a readiness probe endpoint.
    /// Kubernetes uses this to determine if traffic should be routed to the pod.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default is "/health/ready".</param>
    public static IEndpointRouteBuilder MapReadinessProbe(
        this IEndpointRouteBuilder endpoints,
        string path = "/health/ready")
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("critical"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }

    /// <summary>
    /// Maps a startup probe endpoint.
    /// Kubernetes uses this to know when a container application has started.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default is "/health/startup".</param>
    public static IEndpointRouteBuilder MapStartupProbe(
        this IEndpointRouteBuilder endpoints,
        string path = "/health/startup")
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("tier"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }

    /// <summary>
    /// Maps health check endpoints for memory tier monitoring only.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default is "/health/memory".</param>
    public static IEndpointRouteBuilder MapMemoryTierHealthChecks(
        this IEndpointRouteBuilder endpoints,
        string path = "/health/memory")
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("memory"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }

    /// <summary>
    /// Maps health check endpoints for infrastructure monitoring only.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default is "/health/infrastructure".</param>
    public static IEndpointRouteBuilder MapInfrastructureHealthChecks(
        this IEndpointRouteBuilder endpoints,
        string path = "/health/infrastructure")
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("infrastructure"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        return endpoints;
    }
}
