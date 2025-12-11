namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// Service for aggregating and exposing metrics in a dashboard-friendly format.
/// </summary>
public interface IMetricsDashboard
{
    /// <summary>
    /// Gets the current system health summary.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health summary.</returns>
    Task<HealthSummary> GetHealthSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets operation statistics for a time period.
    /// </summary>
    /// <param name="startTime">Start of the period.</param>
    /// <param name="endTime">End of the period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation statistics.</returns>
    Task<OperationStatistics> GetOperationStatisticsAsync(
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Performance metrics.</returns>
    Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets storage statistics.
    /// </summary>
    /// <param name="tenantId">Optional tenant filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Storage statistics.</returns>
    Task<StorageStatistics> GetStorageStatisticsAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets security metrics.
    /// </summary>
    /// <param name="startTime">Start of the period.</param>
    /// <param name="endTime">End of the period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Security metrics.</returns>
    Task<SecurityMetrics> GetSecurityMetricsAsync(
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets time-series data for a specific metric.
    /// </summary>
    /// <param name="metricName">Name of the metric.</param>
    /// <param name="startTime">Start time.</param>
    /// <param name="endTime">End time.</param>
    /// <param name="interval">Aggregation interval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Time-series data points.</returns>
    Task<IReadOnlyList<TimeSeriesDataPoint>> GetTimeSeriesAsync(
        string metricName,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Overall system health summary.
/// </summary>
public sealed class HealthSummary
{
    /// <summary>
    /// Overall health status.
    /// </summary>
    public HealthStatus Status { get; init; }

    /// <summary>
    /// Health score (0.0 to 1.0).
    /// </summary>
    public float HealthScore { get; init; }

    /// <summary>
    /// Individual component health.
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; init; } = [];

    /// <summary>
    /// Active alerts.
    /// </summary>
    public IReadOnlyList<Alert> ActiveAlerts { get; init; } = [];

    /// <summary>
    /// Timestamp of the summary.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// System uptime.
    /// </summary>
    public TimeSpan Uptime { get; init; }
}

/// <summary>
/// Health status levels.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// All systems healthy.
    /// </summary>
    Healthy,

    /// <summary>
    /// Some degradation but functional.
    /// </summary>
    Degraded,

    /// <summary>
    /// Significant issues.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// System is down.
    /// </summary>
    Critical
}

/// <summary>
/// Health status of a component.
/// </summary>
public sealed class ComponentHealth
{
    /// <summary>
    /// Component name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Component status.
    /// </summary>
    public HealthStatus Status { get; init; }

    /// <summary>
    /// Last check timestamp.
    /// </summary>
    public DateTimeOffset LastCheck { get; init; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public double? ResponseTimeMs { get; init; }
}

/// <summary>
/// An active alert.
/// </summary>
public sealed class Alert
{
    /// <summary>
    /// Alert ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Alert severity.
    /// </summary>
    public AlertSeverity Severity { get; init; }

    /// <summary>
    /// Alert title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Alert message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Source component.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// When the alert was triggered.
    /// </summary>
    public DateTimeOffset TriggeredAt { get; init; }

    /// <summary>
    /// Alert metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AlertSeverity
{
    /// <summary>
    /// Informational alert.
    /// </summary>
    Info,

    /// <summary>
    /// Warning alert.
    /// </summary>
    Warning,

    /// <summary>
    /// Error alert.
    /// </summary>
    Error,

    /// <summary>
    /// Critical alert requiring immediate attention.
    /// </summary>
    Critical
}

/// <summary>
/// Operation statistics.
/// </summary>
public sealed class OperationStatistics
{
    /// <summary>
    /// Total operations.
    /// </summary>
    public long TotalOperations { get; init; }

    /// <summary>
    /// Successful operations.
    /// </summary>
    public long SuccessfulOperations { get; init; }

    /// <summary>
    /// Failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Success rate (0.0 to 1.0).
    /// </summary>
    public float SuccessRate { get; init; }

    /// <summary>
    /// Operations by type.
    /// </summary>
    public Dictionary<string, long> OperationsByType { get; init; } = [];

    /// <summary>
    /// Operations per second (average).
    /// </summary>
    public double OperationsPerSecond { get; init; }

    /// <summary>
    /// Peak operations per second.
    /// </summary>
    public double PeakOperationsPerSecond { get; init; }

    /// <summary>
    /// Time period of the statistics.
    /// </summary>
    public TimeSpan Period { get; init; }
}

/// <summary>
/// Performance metrics.
/// </summary>
public sealed class PerformanceMetrics
{
    /// <summary>
    /// Store operation latency (p50).
    /// </summary>
    public double StoreLatencyP50Ms { get; init; }

    /// <summary>
    /// Store operation latency (p95).
    /// </summary>
    public double StoreLatencyP95Ms { get; init; }

    /// <summary>
    /// Store operation latency (p99).
    /// </summary>
    public double StoreLatencyP99Ms { get; init; }

    /// <summary>
    /// Recall operation latency (p50).
    /// </summary>
    public double RecallLatencyP50Ms { get; init; }

    /// <summary>
    /// Recall operation latency (p95).
    /// </summary>
    public double RecallLatencyP95Ms { get; init; }

    /// <summary>
    /// Recall operation latency (p99).
    /// </summary>
    public double RecallLatencyP99Ms { get; init; }

    /// <summary>
    /// Embedding generation latency (p95).
    /// </summary>
    public double EmbeddingLatencyP95Ms { get; init; }

    /// <summary>
    /// Embedding cache hit rate.
    /// </summary>
    public float EmbeddingCacheHitRate { get; init; }

    /// <summary>
    /// Current active operations.
    /// </summary>
    public int ActiveOperations { get; init; }

    /// <summary>
    /// Average similarity score.
    /// </summary>
    public double AvgSimilarityScore { get; init; }

    /// <summary>
    /// Memory utilization percentage.
    /// </summary>
    public float MemoryUtilizationPercent { get; init; }

    /// <summary>
    /// CPU utilization percentage.
    /// </summary>
    public float CpuUtilizationPercent { get; init; }
}

/// <summary>
/// Storage statistics.
/// </summary>
public sealed class StorageStatistics
{
    /// <summary>
    /// Total memories stored.
    /// </summary>
    public long TotalMemories { get; init; }

    /// <summary>
    /// Total storage size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Average memory size in bytes.
    /// </summary>
    public double AvgMemorySizeBytes { get; init; }

    /// <summary>
    /// Memories by type.
    /// </summary>
    public Dictionary<string, long> MemoriesByType { get; init; } = [];

    /// <summary>
    /// Memories by tenant (if multi-tenant).
    /// </summary>
    public Dictionary<string, long> MemoriesByTenant { get; init; } = [];

    /// <summary>
    /// Storage growth rate (bytes per day).
    /// </summary>
    public double GrowthRateBytesPerDay { get; init; }

    /// <summary>
    /// Estimated days until capacity limit.
    /// </summary>
    public int? DaysUntilCapacity { get; init; }

    /// <summary>
    /// Index statistics.
    /// </summary>
    public IndexStatistics? IndexStats { get; init; }
}

/// <summary>
/// Index statistics.
/// </summary>
public sealed class IndexStatistics
{
    /// <summary>
    /// Number of vectors indexed.
    /// </summary>
    public long VectorCount { get; init; }

    /// <summary>
    /// Index size in bytes.
    /// </summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>
    /// Index build status.
    /// </summary>
    public string BuildStatus { get; init; } = "Complete";

    /// <summary>
    /// Last index update.
    /// </summary>
    public DateTimeOffset? LastUpdate { get; init; }
}

/// <summary>
/// Security metrics.
/// </summary>
public sealed class SecurityMetrics
{
    /// <summary>
    /// PII detections count.
    /// </summary>
    public long PiiDetections { get; init; }

    /// <summary>
    /// PII detections by type.
    /// </summary>
    public Dictionary<string, long> PiiByType { get; init; } = [];

    /// <summary>
    /// Injection attempts detected.
    /// </summary>
    public long InjectionAttempts { get; init; }

    /// <summary>
    /// Injection attempts by type.
    /// </summary>
    public Dictionary<string, long> InjectionsByType { get; init; } = [];

    /// <summary>
    /// Rate limit violations.
    /// </summary>
    public long RateLimitViolations { get; init; }

    /// <summary>
    /// Authorization failures.
    /// </summary>
    public long AuthorizationFailures { get; init; }

    /// <summary>
    /// Failed authentication attempts.
    /// </summary>
    public long FailedAuthAttempts { get; init; }

    /// <summary>
    /// Suspicious activity events.
    /// </summary>
    public long SuspiciousActivities { get; init; }

    /// <summary>
    /// Security score (0.0 to 1.0, higher is better).
    /// </summary>
    public float SecurityScore { get; init; }
}

/// <summary>
/// A time-series data point.
/// </summary>
public sealed class TimeSeriesDataPoint
{
    /// <summary>
    /// Timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Value.
    /// </summary>
    public double Value { get; init; }

    /// <summary>
    /// Additional labels.
    /// </summary>
    public Dictionary<string, string> Labels { get; init; } = [];
}
