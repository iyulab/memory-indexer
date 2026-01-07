using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Short-Term Memory tier (L1).
/// Monitors capacity utilization and eviction patterns.
/// </summary>
public class ShortTermMemoryHealthCheck : IHealthCheck
{
    private readonly IShortTermMemory _workingMemory;
    private const double CriticalCapacityThreshold = 0.95; // 95% full
    private const double WarningCapacityThreshold = 0.85;  // 85% full

    public ShortTermMemoryHealthCheck(IShortTermMemory workingMemory)
    {
        _workingMemory = workingMemory ?? throw new ArgumentNullException(nameof(workingMemory));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = _workingMemory.Count;
            var capacity = _workingMemory.Capacity;
            var isFull = _workingMemory.IsFull;
            var utilizationRatio = capacity > 0 ? (double)count / capacity : 0;

            var data = new Dictionary<string, object>
            {
                ["count"] = count,
                ["capacity"] = capacity,
                ["utilizationRatio"] = utilizationRatio,
                ["isFull"] = isFull
            };

            // Critical: Working memory consistently full (may indicate promotion issues)
            if (utilizationRatio >= CriticalCapacityThreshold || isFull)
            {
                return HealthCheckResult.Unhealthy(
                    $"Working memory critically full ({count}/{capacity} = {utilizationRatio:P1}). May indicate promotion bottleneck.",
                    data: data);
            }

            // Degraded: Working memory approaching capacity
            if (utilizationRatio >= WarningCapacityThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Working memory high utilization ({count}/{capacity} = {utilizationRatio:P1})",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Working memory healthy ({count}/{capacity} = {utilizationRatio:P1})",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Working memory health",
                exception: ex);
        }
    }
}
