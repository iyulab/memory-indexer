using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Session Store tier (L2).
/// Monitors storage connectivity and query performance.
/// </summary>
public class SessionStoreHealthCheck : IHealthCheck
{
    private readonly ISessionStore _sessionStore;
    private const int CriticalQueryLatencyMs = 1000;  // 1 second
    private const int WarningQueryLatencyMs = 500;    // 500ms

    public SessionStoreHealthCheck(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
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
            var session = await _sessionStore.GetOrCreateActiveSessionAsync(
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
                    await _sessionStore.DeleteAsync(session.Id, CancellationToken.None);
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
                    $"Session store has critical query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Degraded: Query latency elevated
            if (queryLatencyMs > WarningQueryLatencyMs)
            {
                return HealthCheckResult.Degraded(
                    $"Session store has elevated query latency: {queryLatencyMs:F1}ms",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Session store healthy (latency: {queryLatencyMs:F1}ms)",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Session store health",
                exception: ex);
        }
    }
}
