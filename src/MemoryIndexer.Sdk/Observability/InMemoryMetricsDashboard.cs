using System.Collections.Concurrent;
using System.Diagnostics;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// In-memory implementation of metrics dashboard.
/// Phase v0.11.0: Operational Metrics Dashboard.
/// </summary>
public sealed partial class InMemoryMetricsDashboard : IMetricsDashboard
{
    private readonly ILatencyProfiler? _latencyProfiler;
    private readonly IMemoryGrowthMonitor? _growthMonitor;
    private readonly ILogger<InMemoryMetricsDashboard> _logger;

    private readonly ConcurrentDictionary<string, OperationRecord> _operations = new();
    private readonly ConcurrentQueue<TimeSeriesRecord> _timeSeriesData = new();
    private readonly ConcurrentDictionary<string, SecurityRecord> _securityRecords = new();
    private readonly ConcurrentDictionary<string, ComponentHealth> _componentHealth = new();
    private readonly ConcurrentQueue<Alert> _activeAlerts = new();

    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private long _totalOperations;
    private long _successfulOperations;
    private long _failedOperations;
    private double _peakOpsPerSecond;
    private DateTimeOffset _lastOperationTime = DateTimeOffset.UtcNow;

    // Storage tracking
    private long _totalMemories;
    private long _totalSizeBytes;
    private readonly ConcurrentDictionary<string, long> _memoriesByType = new();
    private readonly ConcurrentDictionary<string, long> _memoriesByTenant = new();

    // Performance tracking
    private readonly ConcurrentQueue<double> _storeLatencies = new();
    private readonly ConcurrentQueue<double> _recallLatencies = new();
    private readonly ConcurrentQueue<double> _embeddingLatencies = new();
    private int _embeddingCacheHits;
    private int _embeddingCacheMisses;
    private int _activeOperations;
    private readonly ConcurrentQueue<double> _similarityScores = new();

    public InMemoryMetricsDashboard(
        ILatencyProfiler? latencyProfiler = null,
        IMemoryGrowthMonitor? growthMonitor = null,
        ILogger<InMemoryMetricsDashboard>? logger = null)
    {
        _latencyProfiler = latencyProfiler;
        _growthMonitor = growthMonitor;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryMetricsDashboard>.Instance;

        // Initialize default component health
        InitializeDefaultComponents();
    }

    private void InitializeDefaultComponents()
    {
        var components = new[] { "VectorStore", "EmbeddingService", "Buffer", "ShortTermMemory", "LongTermStore", "ArchiveStore" };
        foreach (var component in components)
        {
            _componentHealth[component] = new ComponentHealth
            {
                Name = component,
                Status = HealthStatus.Healthy,
                LastCheck = DateTimeOffset.UtcNow
            };
        }
    }

    #region IMetricsDashboard Implementation

    /// <inheritdoc />
    public Task<HealthSummary> GetHealthSummaryAsync(CancellationToken cancellationToken = default)
    {
        var componentsList = _componentHealth.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var unhealthyCount = componentsList.Values.Count(c => c.Status == HealthStatus.Unhealthy || c.Status == HealthStatus.Critical);
        var degradedCount = componentsList.Values.Count(c => c.Status == HealthStatus.Degraded);
        var totalCount = componentsList.Count;

        var healthScore = totalCount > 0
            ? (float)(totalCount - unhealthyCount * 2 - degradedCount) / totalCount
            : 1.0f;
        healthScore = Math.Clamp(healthScore, 0f, 1f);

        var status = unhealthyCount > 0
            ? HealthStatus.Unhealthy
            : degradedCount > 0
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        var summary = new HealthSummary
        {
            Status = status,
            HealthScore = healthScore,
            Components = componentsList,
            ActiveAlerts = _activeAlerts.ToArray(),
            Timestamp = DateTimeOffset.UtcNow,
            Uptime = DateTimeOffset.UtcNow - _startTime
        };

        return Task.FromResult(summary);
    }

    /// <inheritdoc />
    public Task<OperationStatistics> GetOperationStatisticsAsync(
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var period = endTime - startTime;
        var ops = _totalOperations;
        var opsPerSecond = period.TotalSeconds > 0 ? ops / period.TotalSeconds : 0;

        var operationsByType = _operations
            .Where(kvp => kvp.Value.Timestamp >= startTime && kvp.Value.Timestamp <= endTime)
            .GroupBy(kvp => kvp.Value.Type)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var stats = new OperationStatistics
        {
            TotalOperations = ops,
            SuccessfulOperations = _successfulOperations,
            FailedOperations = _failedOperations,
            SuccessRate = ops > 0 ? (float)_successfulOperations / ops : 1.0f,
            OperationsByType = operationsByType,
            OperationsPerSecond = opsPerSecond,
            PeakOperationsPerSecond = _peakOpsPerSecond,
            Period = period
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default)
    {
        var storeLatenciesList = _storeLatencies.ToArray();
        var recallLatenciesList = _recallLatencies.ToArray();
        var embeddingLatenciesList = _embeddingLatencies.ToArray();
        var similarityScoresList = _similarityScores.ToArray();

        var metrics = new PerformanceMetrics
        {
            StoreLatencyP50Ms = CalculatePercentile(storeLatenciesList, 0.50),
            StoreLatencyP95Ms = CalculatePercentile(storeLatenciesList, 0.95),
            StoreLatencyP99Ms = CalculatePercentile(storeLatenciesList, 0.99),
            RecallLatencyP50Ms = CalculatePercentile(recallLatenciesList, 0.50),
            RecallLatencyP95Ms = CalculatePercentile(recallLatenciesList, 0.95),
            RecallLatencyP99Ms = CalculatePercentile(recallLatenciesList, 0.99),
            EmbeddingLatencyP95Ms = CalculatePercentile(embeddingLatenciesList, 0.95),
            EmbeddingCacheHitRate = _embeddingCacheHits + _embeddingCacheMisses > 0
                ? (float)_embeddingCacheHits / (_embeddingCacheHits + _embeddingCacheMisses)
                : 0f,
            ActiveOperations = _activeOperations,
            AvgSimilarityScore = similarityScoresList.Length > 0 ? similarityScoresList.Average() : 0.0,
            MemoryUtilizationPercent = GetMemoryUtilization(),
            CpuUtilizationPercent = GetCpuUtilization()
        };

        return Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public Task<StorageStatistics> GetStorageStatisticsAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var memoriesByTenant = tenantId != null
            ? _memoriesByTenant.Where(kvp => kvp.Key == tenantId).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            : new Dictionary<string, long>(_memoriesByTenant);

        var avgSize = _totalMemories > 0 ? (double)_totalSizeBytes / _totalMemories : 0;

        var stats = new StorageStatistics
        {
            TotalMemories = _totalMemories,
            TotalSizeBytes = _totalSizeBytes,
            AvgMemorySizeBytes = avgSize,
            MemoriesByType = new Dictionary<string, long>(_memoriesByType),
            MemoriesByTenant = memoriesByTenant,
            GrowthRateBytesPerDay = CalculateGrowthRate(),
            DaysUntilCapacity = null, // Could be calculated based on configured limits
            IndexStats = new IndexStatistics
            {
                VectorCount = _totalMemories,
                IndexSizeBytes = _totalSizeBytes,
                BuildStatus = "Complete",
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public Task<SecurityMetrics> GetSecurityMetricsAsync(
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var relevantRecords = _securityRecords.Values
            .Where(r => r.Timestamp >= startTime && r.Timestamp <= endTime)
            .ToList();

        var piiByType = relevantRecords
            .Where(r => r.Type == "PII")
            .GroupBy(r => r.SubType)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var injectionsByType = relevantRecords
            .Where(r => r.Type == "Injection")
            .GroupBy(r => r.SubType)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var totalSecurityEvents = relevantRecords.Count;
        var criticalEvents = relevantRecords.Count(r => r.Severity == "Critical" || r.Severity == "High");
        var securityScore = totalSecurityEvents > 0
            ? 1.0f - (float)criticalEvents / totalSecurityEvents
            : 1.0f;

        var metrics = new SecurityMetrics
        {
            PiiDetections = relevantRecords.Count(r => r.Type == "PII"),
            PiiByType = piiByType,
            InjectionAttempts = relevantRecords.Count(r => r.Type == "Injection"),
            InjectionsByType = injectionsByType,
            RateLimitViolations = relevantRecords.Count(r => r.Type == "RateLimit"),
            AuthorizationFailures = relevantRecords.Count(r => r.Type == "AuthFailure"),
            FailedAuthAttempts = relevantRecords.Count(r => r.Type == "FailedAuth"),
            SuspiciousActivities = relevantRecords.Count(r => r.Type == "Suspicious"),
            SecurityScore = securityScore
        };

        return Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TimeSeriesDataPoint>> GetTimeSeriesAsync(
        string metricName,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        var relevantData = _timeSeriesData
            .Where(r => r.MetricName == metricName && r.Timestamp >= startTime && r.Timestamp <= endTime)
            .OrderBy(r => r.Timestamp)
            .ToList();

        var buckets = new Dictionary<DateTimeOffset, List<double>>();
        var currentBucket = startTime;

        foreach (var record in relevantData)
        {
            while (currentBucket + interval <= record.Timestamp && currentBucket < endTime)
            {
                currentBucket += interval;
            }

            if (!buckets.TryGetValue(currentBucket, out var bucketList))
            {
                bucketList = new List<double>();
                buckets[currentBucket] = bucketList;
            }
            bucketList.Add(record.Value);
        }

        var result = buckets
            .Select(kvp => new TimeSeriesDataPoint
            {
                Timestamp = kvp.Key,
                Value = kvp.Value.Average(),
                Labels = new Dictionary<string, string> { ["metric"] = metricName }
            })
            .OrderBy(p => p.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<TimeSeriesDataPoint>>(result);
    }

    #endregion

    #region Recording Methods

    /// <summary>
    /// Records an operation for metrics tracking.
    /// </summary>
    public void RecordOperation(string operationType, bool success, double latencyMs)
    {
        var id = Guid.NewGuid().ToString();
        _operations[id] = new OperationRecord
        {
            Type = operationType,
            Success = success,
            LatencyMs = latencyMs,
            Timestamp = DateTimeOffset.UtcNow
        };

        Interlocked.Increment(ref _totalOperations);
        if (success)
            Interlocked.Increment(ref _successfulOperations);
        else
            Interlocked.Increment(ref _failedOperations);

        // Track latencies by type
        if (operationType == "Store")
            _storeLatencies.Enqueue(latencyMs);
        else if (operationType == "Recall")
            _recallLatencies.Enqueue(latencyMs);
        else if (operationType == "Embedding")
            _embeddingLatencies.Enqueue(latencyMs);

        // Update peak ops/second
        UpdatePeakOpsPerSecond();

        // Record time series
        RecordTimeSeries($"operation.{operationType}.latency", latencyMs);

        // Limit queue sizes
        LimitQueueSize(_storeLatencies, 10000);
        LimitQueueSize(_recallLatencies, 10000);
        LimitQueueSize(_embeddingLatencies, 10000);
    }

    /// <summary>
    /// Records a store operation.
    /// </summary>
    public void RecordStore(double latencyMs, bool success, int sizeBytes, string? memoryType = null, string? tenantId = null)
    {
        RecordOperation("Store", success, latencyMs);

        if (success)
        {
            Interlocked.Increment(ref _totalMemories);
            Interlocked.Add(ref _totalSizeBytes, sizeBytes);

            if (memoryType != null)
                _memoriesByType.AddOrUpdate(memoryType, 1, (_, count) => count + 1);

            if (tenantId != null)
                _memoriesByTenant.AddOrUpdate(tenantId, 1, (_, count) => count + 1);
        }
    }

    /// <summary>
    /// Records a recall operation.
    /// </summary>
    public void RecordRecall(double latencyMs, bool success, double? similarityScore = null)
    {
        RecordOperation("Recall", success, latencyMs);

        if (similarityScore.HasValue)
            _similarityScores.Enqueue(similarityScore.Value);

        LimitQueueSize(_similarityScores, 10000);
    }

    /// <summary>
    /// Records an embedding operation.
    /// </summary>
    public void RecordEmbedding(double latencyMs, bool cacheHit)
    {
        RecordOperation("Embedding", true, latencyMs);

        if (cacheHit)
            Interlocked.Increment(ref _embeddingCacheHits);
        else
            Interlocked.Increment(ref _embeddingCacheMisses);
    }

    /// <summary>
    /// Records a security event.
    /// </summary>
    public void RecordSecurityEvent(string type, string subType, string severity)
    {
        var id = Guid.NewGuid().ToString();
        _securityRecords[id] = new SecurityRecord
        {
            Type = type,
            SubType = subType,
            Severity = severity,
            Timestamp = DateTimeOffset.UtcNow
        };

        RecordTimeSeries($"security.{type}.count", 1);

        // Create alert for critical security events
        if (severity == "Critical" || severity == "High")
        {
            AddAlert(AlertSeverity.Warning, $"Security: {type}", $"Detected {subType}", "SecurityMonitor");
        }
    }

    /// <summary>
    /// Updates component health status.
    /// </summary>
    public void UpdateComponentHealth(string component, HealthStatus status, string? message = null, double? responseTimeMs = null)
    {
        _componentHealth[component] = new ComponentHealth
        {
            Name = component,
            Status = status,
            LastCheck = DateTimeOffset.UtcNow,
            Message = message,
            ResponseTimeMs = responseTimeMs
        };

        RecordTimeSeries($"health.{component}.status", (int)status);

        // Create alert if unhealthy
        if (status == HealthStatus.Unhealthy || status == HealthStatus.Critical)
        {
            AddAlert(
                status == HealthStatus.Critical ? AlertSeverity.Critical : AlertSeverity.Error,
                $"{component} Health Issue",
                message ?? $"{component} is {status}",
                component);
        }
    }

    /// <summary>
    /// Increments active operation count.
    /// </summary>
    public void BeginOperation()
    {
        Interlocked.Increment(ref _activeOperations);
    }

    /// <summary>
    /// Decrements active operation count.
    /// </summary>
    public void EndOperation()
    {
        Interlocked.Decrement(ref _activeOperations);
    }

    /// <summary>
    /// Adds an alert.
    /// </summary>
    public void AddAlert(AlertSeverity severity, string title, string message, string source)
    {
        var alert = new Alert
        {
            Severity = severity,
            Title = title,
            Message = message,
            Source = source,
            TriggeredAt = DateTimeOffset.UtcNow
        };

        _activeAlerts.Enqueue(alert);

        // Limit alert queue size
        while (_activeAlerts.Count > 100)
        {
            _activeAlerts.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Clears an alert.
    /// </summary>
    public void ClearAlert(Guid alertId)
    {
        // ConcurrentQueue doesn't support removal, so we'd need a different structure
        // For now, alerts will age out
        LogAlertAlertIdClearRequestedAlerts(_logger, alertId);
    }

    #endregion

    #region Private Helpers

    private void RecordTimeSeries(string metricName, double value)
    {
        _timeSeriesData.Enqueue(new TimeSeriesRecord
        {
            MetricName = metricName,
            Value = value,
            Timestamp = DateTimeOffset.UtcNow
        });

        // Limit time series data (keep last 24 hours worth at 1/second max)
        while (_timeSeriesData.Count > 86400)
        {
            _timeSeriesData.TryDequeue(out _);
        }
    }

    private void UpdatePeakOpsPerSecond()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastOperationTime).TotalSeconds;
        if (elapsed > 0 && elapsed < 10)
        {
            var currentOps = 1.0 / elapsed;
            if (currentOps > _peakOpsPerSecond)
            {
                _peakOpsPerSecond = currentOps;
            }
        }
        _lastOperationTime = now;
    }

    private static double CalculatePercentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;

        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)(sorted.Length * percentile);
        index = Math.Min(index, sorted.Length - 1);
        return sorted[index];
    }

    private double CalculateGrowthRate()
    {
        var uptimeDays = (DateTimeOffset.UtcNow - _startTime).TotalDays;
        if (uptimeDays < 0.001) return 0;
        return _totalSizeBytes / uptimeDays;
    }

    private static float GetMemoryUtilization()
    {
        var process = Process.GetCurrentProcess();
        var workingSetMb = process.WorkingSet64 / (1024 * 1024);
        // Assume 4GB as a reasonable max for illustration
        return Math.Min((float)workingSetMb / 4096, 1.0f) * 100;
    }

    private static float GetCpuUtilization()
    {
        // Basic approximation - actual CPU measurement requires more complex tracking
        var process = Process.GetCurrentProcess();
        return (float)Math.Min(process.TotalProcessorTime.TotalMilliseconds / 100000, 100);
    }

    private static void LimitQueueSize<T>(ConcurrentQueue<T> queue, int maxSize)
    {
        while (queue.Count > maxSize)
        {
            queue.TryDequeue(out _);
        }
    }

    #endregion

    #region Private Records

    private sealed class OperationRecord
    {
        public required string Type { get; init; }
        public bool Success { get; init; }
        public double LatencyMs { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    private sealed class TimeSeriesRecord
    {
        public required string MetricName { get; init; }
        public double Value { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    private sealed class SecurityRecord
    {
        public required string Type { get; init; }
        public required string SubType { get; init; }
        public required string Severity { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Alert {AlertId} clear requested - alerts auto-expire")]
    private static partial void LogAlertAlertIdClearRequestedAlerts(ILogger logger, Guid alertId);
}
