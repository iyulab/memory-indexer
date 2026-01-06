using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Recently Buffer tier (Tier 0).
/// Monitors buffer capacity, processing lag, and overall tier health.
/// </summary>
public class RecentlyBufferHealthCheck : IHealthCheck
{
    private readonly IRecentlyBuffer _buffer;
    private const int CriticalProcessingLagSeconds = 120; // 2 minutes
    private const int WarningProcessingLagSeconds = 60;   // 1 minute
    private const int CriticalTokenThreshold = 5000;      // High buffer accumulation
    private const int WarningTokenThreshold = 2000;

    public RecentlyBufferHealthCheck(IRecentlyBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _buffer.GetStats("__health_check_test__");

            // Calculate processing lag from oldest item timestamp
            var processingLag = stats.OldestItemTimestamp.HasValue
                ? (DateTime.UtcNow - stats.OldestItemTimestamp.Value).TotalSeconds
                : 0;

            var data = new Dictionary<string, object>
            {
                ["itemCount"] = stats.ItemCount,
                ["totalTokens"] = stats.TotalTokens,
                ["turnCount"] = stats.TurnCount,
                ["processingLag"] = processingLag,
                ["triggerSatisfied"] = stats.TriggerSatisfied
            };

            // Critical: Processing lag too high (buffer not draining)
            if (processingLag > CriticalProcessingLagSeconds)
            {
                return HealthCheckResult.Unhealthy(
                    $"Recently buffer has critical processing lag: {processingLag:F1}s (oldest entry)",
                    data: data);
            }

            // Critical: Token accumulation too high
            if (stats.TotalTokens > CriticalTokenThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Recently buffer has critical token accumulation: {stats.TotalTokens} tokens",
                    data: data);
            }

            // Degraded: Warning thresholds exceeded
            if (processingLag > WarningProcessingLagSeconds ||
                stats.TotalTokens > WarningTokenThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Recently buffer approaching capacity limits (lag: {processingLag:F1}s, tokens: {stats.TotalTokens})",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Recently buffer healthy ({stats.ItemCount} items, {stats.TotalTokens} tokens)",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Recently buffer health",
                exception: ex);
        }
    }
}
