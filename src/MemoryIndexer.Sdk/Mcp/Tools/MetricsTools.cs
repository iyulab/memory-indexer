using System.ComponentModel;
using MemoryIndexer.Sdk.Observability;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for accessing operational metrics dashboard.
/// Phase v0.11.0: Operational Metrics Dashboard.
/// </summary>
[McpServerToolType]
public class MetricsTools(IMetricsDashboard dashboard)
{
    /// <summary>
    /// Get system health summary including component status and active alerts.
    /// </summary>
    [McpServerTool]
    [Description("Get overall system health summary including component status, health score, and active alerts.")]
    public async Task<HealthSummaryResult> GetHealthSummary(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await dashboard.GetHealthSummaryAsync(cancellationToken);

            return new HealthSummaryResult
            {
                Success = true,
                Status = summary.Status.ToString(),
                HealthScore = summary.HealthScore,
                UptimeMinutes = (int)summary.Uptime.TotalMinutes,
                ComponentCount = summary.Components.Count,
                Components = summary.Components
                    .Select(kv => new ComponentHealthResult
                    {
                        Name = kv.Key,
                        Status = kv.Value.Status.ToString(),
                        Message = kv.Value.Message,
                        ResponseTimeMs = kv.Value.ResponseTimeMs,
                        LastCheck = kv.Value.LastCheck.ToString("O")
                    })
                    .ToList(),
                AlertCount = summary.ActiveAlerts.Count,
                Alerts = summary.ActiveAlerts
                    .Take(10)
                    .Select(a => new AlertResult
                    {
                        Severity = a.Severity.ToString(),
                        Title = a.Title,
                        Message = a.Message,
                        Source = a.Source,
                        TriggeredAt = a.TriggeredAt.ToString("O")
                    })
                    .ToList(),
                Message = summary.Status == HealthStatus.Healthy
                    ? $"System is healthy with {summary.Components.Count} components"
                    : $"System status: {summary.Status} ({summary.ActiveAlerts.Count} active alerts)"
            };
        }
        catch (Exception ex)
        {
            return new HealthSummaryResult
            {
                Success = false,
                Message = $"Failed to get health summary: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get operation statistics for a time period.
    /// </summary>
    [McpServerTool]
    [Description("Get operation statistics including success rate, operation counts, and throughput for a specified time period.")]
    public async Task<OperationStatsResult> GetOperationStats(
        [Description("Hours to look back (default: 1)")] int hours = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endTime = DateTimeOffset.UtcNow;
            var startTime = endTime.AddHours(-hours);

            var stats = await dashboard.GetOperationStatisticsAsync(startTime, endTime, cancellationToken);

            return new OperationStatsResult
            {
                Success = true,
                PeriodHours = hours,
                TotalOperations = stats.TotalOperations,
                SuccessfulOperations = stats.SuccessfulOperations,
                FailedOperations = stats.FailedOperations,
                SuccessRate = stats.SuccessRate,
                SuccessRatePercent = $"{stats.SuccessRate * 100:F1}%",
                OperationsPerSecond = stats.OperationsPerSecond,
                PeakOperationsPerSecond = stats.PeakOperationsPerSecond,
                OperationsByType = stats.OperationsByType
                    .Select(kv => new OperationTypeCount { Type = kv.Key, Count = kv.Value })
                    .ToList(),
                Message = $"Statistics for last {hours} hour(s): {stats.TotalOperations} operations, {stats.SuccessRate * 100:F1}% success rate"
            };
        }
        catch (Exception ex)
        {
            return new OperationStatsResult
            {
                Success = false,
                Message = $"Failed to get operation stats: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get performance metrics including latencies and cache hit rates.
    /// </summary>
    [McpServerTool]
    [Description("Get performance metrics including store/recall latencies, cache hit rates, and resource utilization.")]
    public async Task<PerformanceMetricsResult> GetPerformanceMetrics(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await dashboard.GetPerformanceMetricsAsync(cancellationToken);

            return new PerformanceMetricsResult
            {
                Success = true,
                StoreLatencyP50Ms = metrics.StoreLatencyP50Ms,
                StoreLatencyP95Ms = metrics.StoreLatencyP95Ms,
                StoreLatencyP99Ms = metrics.StoreLatencyP99Ms,
                RecallLatencyP50Ms = metrics.RecallLatencyP50Ms,
                RecallLatencyP95Ms = metrics.RecallLatencyP95Ms,
                RecallLatencyP99Ms = metrics.RecallLatencyP99Ms,
                EmbeddingLatencyP95Ms = metrics.EmbeddingLatencyP95Ms,
                EmbeddingCacheHitRate = metrics.EmbeddingCacheHitRate,
                EmbeddingCacheHitRatePercent = $"{metrics.EmbeddingCacheHitRate * 100:F1}%",
                ActiveOperations = metrics.ActiveOperations,
                AvgSimilarityScore = metrics.AvgSimilarityScore,
                MemoryUtilizationPercent = metrics.MemoryUtilizationPercent,
                CpuUtilizationPercent = metrics.CpuUtilizationPercent,
                Message = $"Recall P95: {metrics.RecallLatencyP95Ms:F0}ms, Cache: {metrics.EmbeddingCacheHitRate * 100:F0}% hit rate"
            };
        }
        catch (Exception ex)
        {
            return new PerformanceMetricsResult
            {
                Success = false,
                Message = $"Failed to get performance metrics: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get storage statistics including memory counts and sizes.
    /// </summary>
    [McpServerTool]
    [Description("Get storage statistics including total memories, sizes, growth rate, and distribution by type/tenant.")]
    public async Task<StorageStatsResult> GetStorageStats(
        [Description("Optional tenant ID to filter by")] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await dashboard.GetStorageStatisticsAsync(tenantId, cancellationToken);

            return new StorageStatsResult
            {
                Success = true,
                TenantFilter = tenantId,
                TotalMemories = stats.TotalMemories,
                TotalSizeBytes = stats.TotalSizeBytes,
                TotalSizeFormatted = FormatBytes(stats.TotalSizeBytes),
                AvgMemorySizeBytes = stats.AvgMemorySizeBytes,
                GrowthRateBytesPerDay = stats.GrowthRateBytesPerDay,
                GrowthRateFormatted = $"{FormatBytes((long)stats.GrowthRateBytesPerDay)}/day",
                DaysUntilCapacity = stats.DaysUntilCapacity,
                MemoriesByType = stats.MemoriesByType
                    .Select(kv => new MemoryTypeCount { Type = kv.Key, Count = kv.Value })
                    .ToList(),
                MemoriesByTenant = stats.MemoriesByTenant
                    .Select(kv => new MemoryTenantCount { TenantId = kv.Key, Count = kv.Value })
                    .ToList(),
                IndexStats = stats.IndexStats != null
                    ? new IndexStatsResult
                    {
                        VectorCount = stats.IndexStats.VectorCount,
                        IndexSizeBytes = stats.IndexStats.IndexSizeBytes,
                        IndexSizeFormatted = FormatBytes(stats.IndexStats.IndexSizeBytes),
                        BuildStatus = stats.IndexStats.BuildStatus
                    }
                    : null,
                Message = $"Total: {stats.TotalMemories} memories ({FormatBytes(stats.TotalSizeBytes)})"
            };
        }
        catch (Exception ex)
        {
            return new StorageStatsResult
            {
                Success = false,
                Message = $"Failed to get storage stats: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get security metrics including PII detections and injection attempts.
    /// </summary>
    [McpServerTool]
    [Description("Get security metrics including PII detections, injection attempts, and security score for a specified time period.")]
    public async Task<SecurityMetricsResult> GetSecurityMetrics(
        [Description("Hours to look back (default: 24)")] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endTime = DateTimeOffset.UtcNow;
            var startTime = endTime.AddHours(-hours);

            var metrics = await dashboard.GetSecurityMetricsAsync(startTime, endTime, cancellationToken);

            return new SecurityMetricsResult
            {
                Success = true,
                PeriodHours = hours,
                SecurityScore = metrics.SecurityScore,
                SecurityScorePercent = $"{metrics.SecurityScore * 100:F0}%",
                PiiDetections = metrics.PiiDetections,
                PiiByType = metrics.PiiByType
                    .Select(kv => new SecurityEventCount { Type = kv.Key, Count = kv.Value })
                    .ToList(),
                InjectionAttempts = metrics.InjectionAttempts,
                InjectionsByType = metrics.InjectionsByType
                    .Select(kv => new SecurityEventCount { Type = kv.Key, Count = kv.Value })
                    .ToList(),
                RateLimitViolations = metrics.RateLimitViolations,
                AuthorizationFailures = metrics.AuthorizationFailures,
                FailedAuthAttempts = metrics.FailedAuthAttempts,
                SuspiciousActivities = metrics.SuspiciousActivities,
                Message = metrics.SecurityScore >= 0.9f
                    ? $"Security score: {metrics.SecurityScore * 100:F0}% (Good)"
                    : $"Security score: {metrics.SecurityScore * 100:F0}% ({metrics.InjectionAttempts} injection attempts, {metrics.PiiDetections} PII detections)"
            };
        }
        catch (Exception ex)
        {
            return new SecurityMetricsResult
            {
                Success = false,
                Message = $"Failed to get security metrics: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get time-series data for a specific metric.
    /// </summary>
    [McpServerTool]
    [Description("Get time-series data for a specific metric to visualize trends over time.")]
    public async Task<TimeSeriesResult> GetMetricTimeSeries(
        [Description("Metric name (e.g., operation.Store.latency, health.VectorStore.status)")] string metricName,
        [Description("Hours to look back (default: 1)")] int hours = 1,
        [Description("Interval in minutes for data aggregation (default: 5)")] int intervalMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endTime = DateTimeOffset.UtcNow;
            var startTime = endTime.AddHours(-hours);
            var interval = TimeSpan.FromMinutes(intervalMinutes);

            var data = await dashboard.GetTimeSeriesAsync(metricName, startTime, endTime, interval, cancellationToken);

            return new TimeSeriesResult
            {
                Success = true,
                MetricName = metricName,
                PeriodHours = hours,
                IntervalMinutes = intervalMinutes,
                DataPointCount = data.Count,
                DataPoints = data
                    .Select(p => new TimeSeriesPointResult
                    {
                        Timestamp = p.Timestamp.ToString("O"),
                        Value = p.Value
                    })
                    .ToList(),
                Summary = data.Count > 0
                    ? new TimeSeriesSummary
                    {
                        Min = data.Min(p => p.Value),
                        Max = data.Max(p => p.Value),
                        Avg = data.Average(p => p.Value),
                        Latest = data[^1].Value
                    }
                    : null,
                Message = data.Count > 0
                    ? $"Retrieved {data.Count} data points for {metricName}"
                    : $"No data available for {metricName} in the specified time range"
            };
        }
        catch (Exception ex)
        {
            return new TimeSeriesResult
            {
                Success = false,
                MetricName = metricName,
                Message = $"Failed to get time series data: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get a comprehensive metrics overview combining key stats from all categories.
    /// </summary>
    [McpServerTool]
    [Description("Get a comprehensive dashboard overview combining health, performance, storage, and security metrics.")]
    public async Task<DashboardOverviewResult> GetDashboardOverview(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            var healthTask = dashboard.GetHealthSummaryAsync(cancellationToken);
            var operationsTask = dashboard.GetOperationStatisticsAsync(now.AddHours(-1), now, cancellationToken);
            var performanceTask = dashboard.GetPerformanceMetricsAsync(cancellationToken);
            var storageTask = dashboard.GetStorageStatisticsAsync(cancellationToken: cancellationToken);
            var securityTask = dashboard.GetSecurityMetricsAsync(now.AddHours(-24), now, cancellationToken);

            await Task.WhenAll(healthTask, operationsTask, performanceTask, storageTask, securityTask);

            var health = await healthTask;
            var operations = await operationsTask;
            var performance = await performanceTask;
            var storage = await storageTask;
            var security = await securityTask;

            return new DashboardOverviewResult
            {
                Success = true,
                Timestamp = now.ToString("O"),
                Health = new HealthOverview
                {
                    Status = health.Status.ToString(),
                    Score = health.HealthScore,
                    UptimeMinutes = (int)health.Uptime.TotalMinutes,
                    AlertCount = health.ActiveAlerts.Count
                },
                Operations = new OperationsOverview
                {
                    TotalLastHour = operations.TotalOperations,
                    SuccessRate = operations.SuccessRate,
                    OpsPerSecond = operations.OperationsPerSecond
                },
                Performance = new PerformanceOverview
                {
                    RecallP95Ms = performance.RecallLatencyP95Ms,
                    CacheHitRate = performance.EmbeddingCacheHitRate,
                    AvgSimilarity = performance.AvgSimilarityScore
                },
                Storage = new StorageOverview
                {
                    TotalMemories = storage.TotalMemories,
                    TotalSize = FormatBytes(storage.TotalSizeBytes),
                    GrowthRate = $"{FormatBytes((long)storage.GrowthRateBytesPerDay)}/day"
                },
                Security = new SecurityOverview
                {
                    Score = security.SecurityScore,
                    PiiDetections24h = security.PiiDetections,
                    InjectionAttempts24h = security.InjectionAttempts
                },
                Message = $"Dashboard: {health.Status}, {operations.TotalOperations} ops/hr, {storage.TotalMemories} memories"
            };
        }
        catch (Exception ex)
        {
            return new DashboardOverviewResult
            {
                Success = false,
                Message = $"Failed to get dashboard overview: {ex.Message}"
            };
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }
}

#region Result Types

/// <summary>
/// Health summary result.
/// </summary>
public class HealthSummaryResult
{
    public bool Success { get; init; }
    public string? Status { get; init; }
    public float HealthScore { get; init; }
    public int UptimeMinutes { get; init; }
    public int ComponentCount { get; init; }
    public List<ComponentHealthResult>? Components { get; init; }
    public int AlertCount { get; init; }
    public List<AlertResult>? Alerts { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Component health result.
/// </summary>
public class ComponentHealthResult
{
    public string? Name { get; init; }
    public string? Status { get; init; }
    public string? Message { get; init; }
    public double? ResponseTimeMs { get; init; }
    public string? LastCheck { get; init; }
}

/// <summary>
/// Alert result.
/// </summary>
public class AlertResult
{
    public string? Severity { get; init; }
    public string? Title { get; init; }
    public string? Message { get; init; }
    public string? Source { get; init; }
    public string? TriggeredAt { get; init; }
}

/// <summary>
/// Operation statistics result.
/// </summary>
public class OperationStatsResult
{
    public bool Success { get; init; }
    public int PeriodHours { get; init; }
    public long TotalOperations { get; init; }
    public long SuccessfulOperations { get; init; }
    public long FailedOperations { get; init; }
    public float SuccessRate { get; init; }
    public string? SuccessRatePercent { get; init; }
    public double OperationsPerSecond { get; init; }
    public double PeakOperationsPerSecond { get; init; }
    public List<OperationTypeCount>? OperationsByType { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Operation type count.
/// </summary>
public class OperationTypeCount
{
    public string? Type { get; init; }
    public long Count { get; init; }
}

/// <summary>
/// Performance metrics result.
/// </summary>
public class PerformanceMetricsResult
{
    public bool Success { get; init; }
    public double StoreLatencyP50Ms { get; init; }
    public double StoreLatencyP95Ms { get; init; }
    public double StoreLatencyP99Ms { get; init; }
    public double RecallLatencyP50Ms { get; init; }
    public double RecallLatencyP95Ms { get; init; }
    public double RecallLatencyP99Ms { get; init; }
    public double EmbeddingLatencyP95Ms { get; init; }
    public float EmbeddingCacheHitRate { get; init; }
    public string? EmbeddingCacheHitRatePercent { get; init; }
    public int ActiveOperations { get; init; }
    public double AvgSimilarityScore { get; init; }
    public float MemoryUtilizationPercent { get; init; }
    public float CpuUtilizationPercent { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Storage statistics result.
/// </summary>
public class StorageStatsResult
{
    public bool Success { get; init; }
    public string? TenantFilter { get; init; }
    public long TotalMemories { get; init; }
    public long TotalSizeBytes { get; init; }
    public string? TotalSizeFormatted { get; init; }
    public double AvgMemorySizeBytes { get; init; }
    public double GrowthRateBytesPerDay { get; init; }
    public string? GrowthRateFormatted { get; init; }
    public int? DaysUntilCapacity { get; init; }
    public List<MemoryTypeCount>? MemoriesByType { get; init; }
    public List<MemoryTenantCount>? MemoriesByTenant { get; init; }
    public IndexStatsResult? IndexStats { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Memory type count.
/// </summary>
public class MemoryTypeCount
{
    public string? Type { get; init; }
    public long Count { get; init; }
}

/// <summary>
/// Memory tenant count.
/// </summary>
public class MemoryTenantCount
{
    public string? TenantId { get; init; }
    public long Count { get; init; }
}

/// <summary>
/// Index statistics result.
/// </summary>
public class IndexStatsResult
{
    public long VectorCount { get; init; }
    public long IndexSizeBytes { get; init; }
    public string? IndexSizeFormatted { get; init; }
    public string? BuildStatus { get; init; }
}

/// <summary>
/// Security metrics result.
/// </summary>
public class SecurityMetricsResult
{
    public bool Success { get; init; }
    public int PeriodHours { get; init; }
    public float SecurityScore { get; init; }
    public string? SecurityScorePercent { get; init; }
    public long PiiDetections { get; init; }
    public List<SecurityEventCount>? PiiByType { get; init; }
    public long InjectionAttempts { get; init; }
    public List<SecurityEventCount>? InjectionsByType { get; init; }
    public long RateLimitViolations { get; init; }
    public long AuthorizationFailures { get; init; }
    public long FailedAuthAttempts { get; init; }
    public long SuspiciousActivities { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Security event count.
/// </summary>
public class SecurityEventCount
{
    public string? Type { get; init; }
    public long Count { get; init; }
}

/// <summary>
/// Time series result.
/// </summary>
public class TimeSeriesResult
{
    public bool Success { get; init; }
    public string? MetricName { get; init; }
    public int PeriodHours { get; init; }
    public int IntervalMinutes { get; init; }
    public int DataPointCount { get; init; }
    public List<TimeSeriesPointResult>? DataPoints { get; init; }
    public TimeSeriesSummary? Summary { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Time series data point result.
/// </summary>
public class TimeSeriesPointResult
{
    public string? Timestamp { get; init; }
    public double Value { get; init; }
}

/// <summary>
/// Time series summary.
/// </summary>
public class TimeSeriesSummary
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double Avg { get; init; }
    public double Latest { get; init; }
}

/// <summary>
/// Dashboard overview result.
/// </summary>
public class DashboardOverviewResult
{
    public bool Success { get; init; }
    public string? Timestamp { get; init; }
    public HealthOverview? Health { get; init; }
    public OperationsOverview? Operations { get; init; }
    public PerformanceOverview? Performance { get; init; }
    public StorageOverview? Storage { get; init; }
    public SecurityOverview? Security { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Health overview.
/// </summary>
public class HealthOverview
{
    public string? Status { get; init; }
    public float Score { get; init; }
    public int UptimeMinutes { get; init; }
    public int AlertCount { get; init; }
}

/// <summary>
/// Operations overview.
/// </summary>
public class OperationsOverview
{
    public long TotalLastHour { get; init; }
    public float SuccessRate { get; init; }
    public double OpsPerSecond { get; init; }
}

/// <summary>
/// Performance overview.
/// </summary>
public class PerformanceOverview
{
    public double RecallP95Ms { get; init; }
    public float CacheHitRate { get; init; }
    public double AvgSimilarity { get; init; }
}

/// <summary>
/// Storage overview.
/// </summary>
public class StorageOverview
{
    public long TotalMemories { get; init; }
    public string? TotalSize { get; init; }
    public string? GrowthRate { get; init; }
}

/// <summary>
/// Security overview.
/// </summary>
public class SecurityOverview
{
    public float Score { get; init; }
    public long PiiDetections24h { get; init; }
    public long InjectionAttempts24h { get; init; }
}

#endregion
