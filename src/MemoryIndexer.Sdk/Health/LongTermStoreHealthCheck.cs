using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Episodic Store tier (Tier 2).
/// Monitors storage connectivity and query performance.
/// Implements Tulving's Episodic Memory System health monitoring.
/// </summary>
public class LongTermStoreHealthCheck : IHealthCheck
{
    private readonly ILongTermStore _episodicStore;
    private const int CriticalQueryLatencyMs = 1000;  // 1 second
    private const int WarningQueryLatencyMs = 500;    // 500ms

    public LongTermStoreHealthCheck(ILongTermStore episodicStore)
    {
        _episodicStore = episodicStore ?? throw new ArgumentNullException(nameof(episodicStore));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test connectivity and query performance with a test user
            var testUserId = "__health_check_test__";
            var startTime = DateTimeOffset.UtcNow;

            // Perform a lightweight operation to test connectivity
            var session = await _episodicStore.GetOrCreateActiveSessionAsync(
                testUserId,
                cancellationToken);

            var queryLatencyMs = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

            var data = new Dictionary<string, object>
            {
                ["queryLatencyMs"] = queryLatencyMs,
                ["sessionId"] = session.Id,
                ["timestamp"] = DateTimeOffset.UtcNow
            };

            // Clean up test session (fire-and-forget, don't affect health status)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _episodicStore.DeleteAsync(session.Id, CancellationToken.None);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            });

            // Critical: Query latency too high
            if (queryLatencyMs > CriticalQueryLatencyMs)
            {
                return HealthCheckResult.Unhealthy(
                    $"Episodic store has critical query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Degraded: Query latency elevated
            if (queryLatencyMs > WarningQueryLatencyMs)
            {
                return HealthCheckResult.Degraded(
                    $"Episodic store has elevated query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Episodic store healthy (latency: {queryLatencyMs:F1}ms)",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Episodic store health",
                exception: ex);
        }
    }
}
