using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Vector Database (SQLite-vec / Qdrant).
/// Monitors storage connectivity and query performance.
/// </summary>
public class VectorDbHealthCheck : IHealthCheck
{
    private readonly IMemoryStore _memoryStore;
    private const int CriticalQueryLatencyMs = 500;
    private const int WarningQueryLatencyMs = 200;

    public VectorDbHealthCheck(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow;

            // Test connectivity with a lightweight query
            var testQuery = Enumerable.Repeat(0.1f, 1024).ToArray();

            var results = await _memoryStore.SearchAsync(
                queryEmbedding: testQuery,
                options: new() { UserId = "__health_check_test__", Limit = 1, MinScore = 0.0f },
                cancellationToken: cancellationToken);

            var queryLatencyMs = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

            var data = new Dictionary<string, object>
            {
                ["queryLatencyMs"] = queryLatencyMs,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["storeType"] = _memoryStore.GetType().Name
            };

            // Critical: Query latency too high
            if (queryLatencyMs > CriticalQueryLatencyMs)
            {
                return HealthCheckResult.Unhealthy(
                    $"Vector DB has critical query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Degraded: Query latency elevated
            if (queryLatencyMs > WarningQueryLatencyMs)
            {
                return HealthCheckResult.Degraded(
                    $"Vector DB has elevated query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Vector DB healthy (latency: {queryLatencyMs:F1}ms, type: {data["storeType"]})",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to connect to Vector DB",
                exception: ex);
        }
    }
}
