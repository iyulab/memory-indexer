using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// REST API controller for performance monitoring and metrics.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MetricsController : ControllerBase
{
    private readonly IMemoryPressureMonitor _pressureMonitor;
    private readonly ILatencyProfiler _latencyProfiler;
    private readonly IMemoryGrowthMonitor _growthMonitor;
    private readonly ILogger<MetricsController> _logger;
    private const string DefaultUserId = "default";

    public MetricsController(
        IMemoryPressureMonitor pressureMonitor,
        ILatencyProfiler latencyProfiler,
        IMemoryGrowthMonitor growthMonitor,
        ILogger<MetricsController> logger)
    {
        _pressureMonitor = pressureMonitor ?? throw new ArgumentNullException(nameof(pressureMonitor));
        _latencyProfiler = latencyProfiler ?? throw new ArgumentNullException(nameof(latencyProfiler));
        _growthMonitor = growthMonitor ?? throw new ArgumentNullException(nameof(growthMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get system memory pressure information.
    /// </summary>
    /// <returns>Memory pressure statistics</returns>
    [HttpGet("memory-pressure")]
    [ProducesResponseType(typeof(MemoryPressureResponse), StatusCodes.Status200OK)]
    public IActionResult GetMemoryPressure()
    {
        var info = _pressureMonitor.GetMemoryInfo();

        return Ok(new MemoryPressureResponse
        {
            Level = info.Level.ToString(),
            LevelValue = (int)info.Level,
            TotalAvailableMemoryMb = info.TotalAvailableMemoryBytes / (1024.0 * 1024.0),
            MemoryLoadMb = info.MemoryLoadBytes / (1024.0 * 1024.0),
            HeapSizeMb = info.HeapSizeBytes / (1024.0 * 1024.0),
            UtilizationPercentage = info.UtilizationPercentage * 100,
            Gen0Collections = info.Gen0Collections,
            Gen1Collections = info.Gen1Collections,
            Gen2Collections = info.Gen2Collections,
            IsUnderPressure = _pressureMonitor.IsUnderPressure()
        });
    }

    /// <summary>
    /// Get latency metrics for a user.
    /// </summary>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="tier">Optional tier filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latency metrics</returns>
    [HttpGet("latency")]
    [ProducesResponseType(typeof(LatencyMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatencyMetrics(
        [FromQuery] string? userId = null,
        [FromQuery] string? tier = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            userId ??= DefaultUserId;
            var metrics = await _latencyProfiler.GetMetricsAsync(userId, tier, cancellationToken);

            _logger.LogDebug("Retrieved {Count} latency metrics for user {UserId}", metrics.Count, userId);

            return Ok(new LatencyMetricsResponse
            {
                UserId = userId,
                Tier = tier,
                MetricsCount = metrics.Count,
                Metrics = metrics.Select(m => new LatencyMetricInfo
                {
                    Tier = m.Tier,
                    TotalQueries = m.TotalQueries,
                    AverageLatencyMs = m.AverageLatencyMs,
                    MinLatencyMs = m.MinLatencyMs,
                    MaxLatencyMs = m.MaxLatencyMs,
                    P50LatencyMs = m.P50LatencyMs,
                    P95LatencyMs = m.P95LatencyMs,
                    P99LatencyMs = m.P99LatencyMs,
                    CacheHitRate = m.CacheHitRate,
                    EmbeddingCacheHits = m.EmbeddingCacheHits,
                    EmbeddingCacheMisses = m.EmbeddingCacheMisses,
                    QueryCacheHits = m.QueryCacheHits,
                    QueryCacheMisses = m.QueryCacheMisses,
                    BudgetExceededCount = m.BudgetExceededCount,
                    LatencyBudgetMs = m.LatencyBudgetMs,
                    BudgetComplianceRate = m.BudgetComplianceRate,
                    ComponentLatencies = m.ComponentLatencies,
                    LastUpdated = m.LastUpdated
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latency metrics");
            return StatusCode(500, new { error = "Failed to get latency metrics", details = ex.Message });
        }
    }

    /// <summary>
    /// Get memory growth metrics for a user.
    /// </summary>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Growth metrics</returns>
    [HttpGet("growth")]
    [ProducesResponseType(typeof(MemoryGrowthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGrowthMetrics(
        [FromQuery] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            userId ??= DefaultUserId;
            var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId, cancellationToken);

            _logger.LogDebug("Retrieved growth metrics for user {UserId}: round {Round}, growth rate {Rate}",
                userId, metrics.CurrentRound, metrics.CurrentGrowthRate);

            return Ok(new MemoryGrowthResponse
            {
                UserId = metrics.UserId,
                CurrentRound = metrics.CurrentRound,
                MemoriesStoredThisRound = metrics.MemoriesStoredThisRound,
                MemoriesFilteredThisRound = metrics.MemoriesFilteredThisRound,
                AverageMemoriesPerRound = metrics.AverageMemoriesPerRound,
                CurrentGrowthRate = metrics.CurrentGrowthRate,
                MaxAllowedGrowthRate = metrics.MaxAllowedGrowthRate,
                ExceedsThreshold = metrics.ExceedsThreshold,
                FilterReasons = metrics.FilterReasons,
                Timestamp = metrics.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get growth metrics");
            return StatusCode(500, new { error = "Failed to get growth metrics", details = ex.Message });
        }
    }

    /// <summary>
    /// Get comprehensive dashboard metrics.
    /// </summary>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated metrics for dashboard</returns>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardMetrics(
        [FromQuery] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            userId ??= DefaultUserId;

            // Get all metrics in parallel
            var pressureInfo = _pressureMonitor.GetMemoryInfo();
            var latencyTask = _latencyProfiler.GetMetricsAsync(userId, cancellationToken: cancellationToken);
            var growthTask = _growthMonitor.GetGrowthMetricsAsync(userId, cancellationToken);

            await Task.WhenAll(latencyTask, growthTask);

            var latencyMetrics = latencyTask.Result;
            var growthMetrics = growthTask.Result;

            // Calculate aggregated latency stats
            var avgLatency = latencyMetrics.Count > 0
                ? latencyMetrics.Average(m => m.AverageLatencyMs)
                : 0;
            var avgCacheHitRate = latencyMetrics.Count > 0
                ? latencyMetrics.Average(m => m.CacheHitRate)
                : 0;
            var totalQueries = latencyMetrics.Sum(m => m.TotalQueries);

            return Ok(new DashboardResponse
            {
                UserId = userId,
                Timestamp = DateTime.UtcNow,

                // Memory Pressure Summary
                MemoryPressure = new MemoryPressureSummary
                {
                    Level = pressureInfo.Level.ToString(),
                    UtilizationPercent = pressureInfo.UtilizationPercentage * 100,
                    IsUnderPressure = _pressureMonitor.IsUnderPressure(),
                    HeapSizeMb = pressureInfo.HeapSizeBytes / (1024.0 * 1024.0)
                },

                // Latency Summary
                Latency = new LatencySummary
                {
                    TotalQueries = totalQueries,
                    AverageLatencyMs = avgLatency,
                    CacheHitRate = avgCacheHitRate,
                    TierCount = latencyMetrics.Count
                },

                // Growth Summary
                Growth = new GrowthSummary
                {
                    CurrentRound = growthMetrics.CurrentRound,
                    CurrentGrowthRate = growthMetrics.CurrentGrowthRate,
                    MaxAllowedGrowthRate = growthMetrics.MaxAllowedGrowthRate,
                    ExceedsThreshold = growthMetrics.ExceedsThreshold,
                    TotalFiltered = growthMetrics.MemoriesFilteredThisRound
                },

                // Health Status
                Health = new HealthSummary
                {
                    OverallStatus = DetermineOverallHealth(pressureInfo, avgLatency, growthMetrics),
                    Issues = GetHealthIssues(pressureInfo, latencyMetrics, growthMetrics)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard metrics");
            return StatusCode(500, new { error = "Failed to get dashboard metrics", details = ex.Message });
        }
    }

    /// <summary>
    /// Reset latency metrics for a user.
    /// </summary>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success status</returns>
    [HttpPost("latency/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetLatencyMetrics(
        [FromQuery] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            userId ??= DefaultUserId;
            await _latencyProfiler.ResetMetricsAsync(userId, cancellationToken);

            _logger.LogInformation("Reset latency metrics for user {UserId}", userId);

            return Ok(new { message = "Latency metrics reset successfully", userId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset latency metrics");
            return StatusCode(500, new { error = "Failed to reset latency metrics", details = ex.Message });
        }
    }

    private static string DetermineOverallHealth(
        MemoryPressureInfo pressure,
        double avgLatency,
        MemoryGrowthMetrics growth)
    {
        // Critical conditions
        if (pressure.Level == MemoryPressureLevel.Critical) return "Critical";
        if (avgLatency > 1000) return "Critical"; // > 1 second average
        if (growth.ExceedsThreshold && growth.CurrentGrowthRate > growth.MaxAllowedGrowthRate * 2) return "Critical";

        // Degraded conditions
        if (pressure.Level == MemoryPressureLevel.High) return "Degraded";
        if (avgLatency > 500) return "Degraded"; // > 500ms average
        if (growth.ExceedsThreshold) return "Degraded";

        // Warning conditions
        if (pressure.Level == MemoryPressureLevel.Medium) return "Warning";
        if (avgLatency > 200) return "Warning"; // > 200ms average

        return "Healthy";
    }

    private static List<string> GetHealthIssues(
        MemoryPressureInfo pressure,
        IReadOnlyList<LatencyMetrics> latencyMetrics,
        MemoryGrowthMetrics growth)
    {
        var issues = new List<string>();

        if (pressure.Level >= MemoryPressureLevel.High)
            issues.Add($"High memory pressure: {pressure.UtilizationPercentage * 100:F1}% utilized");

        var highLatencyTiers = latencyMetrics.Where(m => m.AverageLatencyMs > 500).ToList();
        if (highLatencyTiers.Count > 0)
            issues.Add($"High latency in {highLatencyTiers.Count} tier(s): avg {highLatencyTiers.Average(m => m.AverageLatencyMs):F1}ms");

        var lowCacheHitTiers = latencyMetrics.Where(m => m.CacheHitRate < 0.5 && m.TotalQueries > 10).ToList();
        if (lowCacheHitTiers.Count > 0)
            issues.Add($"Low cache hit rate in {lowCacheHitTiers.Count} tier(s)");

        if (growth.ExceedsThreshold)
            issues.Add($"Memory growth rate ({growth.CurrentGrowthRate:F1}) exceeds threshold ({growth.MaxAllowedGrowthRate:F1})");

        if (pressure.Gen2Collections > 10)
            issues.Add($"Frequent Gen2 GC collections: {pressure.Gen2Collections}");

        return issues;
    }
}

#region Response Models

public record MemoryPressureResponse
{
    public string Level { get; init; } = string.Empty;
    public int LevelValue { get; init; }
    public double TotalAvailableMemoryMb { get; init; }
    public double MemoryLoadMb { get; init; }
    public double HeapSizeMb { get; init; }
    public double UtilizationPercentage { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public bool IsUnderPressure { get; init; }
}

public record LatencyMetricsResponse
{
    public string UserId { get; init; } = string.Empty;
    public string? Tier { get; init; }
    public int MetricsCount { get; init; }
    public List<LatencyMetricInfo>? Metrics { get; init; }
}

public record LatencyMetricInfo
{
    public string Tier { get; init; } = string.Empty;
    public int TotalQueries { get; init; }
    public double AverageLatencyMs { get; init; }
    public double MinLatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
    public double P50LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public double CacheHitRate { get; init; }
    public int EmbeddingCacheHits { get; init; }
    public int EmbeddingCacheMisses { get; init; }
    public int QueryCacheHits { get; init; }
    public int QueryCacheMisses { get; init; }
    public int BudgetExceededCount { get; init; }
    public double LatencyBudgetMs { get; init; }
    public double BudgetComplianceRate { get; init; }
    public Dictionary<string, double>? ComponentLatencies { get; init; }
    public DateTime LastUpdated { get; init; }
}

public record MemoryGrowthResponse
{
    public string UserId { get; init; } = string.Empty;
    public int CurrentRound { get; init; }
    public int MemoriesStoredThisRound { get; init; }
    public int MemoriesFilteredThisRound { get; init; }
    public float AverageMemoriesPerRound { get; init; }
    public float CurrentGrowthRate { get; init; }
    public float MaxAllowedGrowthRate { get; init; }
    public bool ExceedsThreshold { get; init; }
    public Dictionary<string, int>? FilterReasons { get; init; }
    public DateTime Timestamp { get; init; }
}

public record DashboardResponse
{
    public string UserId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public MemoryPressureSummary? MemoryPressure { get; init; }
    public LatencySummary? Latency { get; init; }
    public GrowthSummary? Growth { get; init; }
    public HealthSummary? Health { get; init; }
}

public record MemoryPressureSummary
{
    public string Level { get; init; } = string.Empty;
    public double UtilizationPercent { get; init; }
    public bool IsUnderPressure { get; init; }
    public double HeapSizeMb { get; init; }
}

public record LatencySummary
{
    public int TotalQueries { get; init; }
    public double AverageLatencyMs { get; init; }
    public double CacheHitRate { get; init; }
    public int TierCount { get; init; }
}

public record GrowthSummary
{
    public int CurrentRound { get; init; }
    public float CurrentGrowthRate { get; init; }
    public float MaxAllowedGrowthRate { get; init; }
    public bool ExceedsThreshold { get; init; }
    public int TotalFiltered { get; init; }
}

public record HealthSummary
{
    public string OverallStatus { get; init; } = string.Empty;
    public List<string>? Issues { get; init; }
}

#endregion
