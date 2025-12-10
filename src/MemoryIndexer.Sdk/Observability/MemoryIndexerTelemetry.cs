using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// Centralized telemetry definitions for Memory Indexer.
/// Provides ActivitySource for tracing and Meter for metrics.
/// </summary>
public static class MemoryIndexerTelemetry
{
    /// <summary>
    /// Service name used for telemetry identification.
    /// </summary>
    public const string ServiceName = "MemoryIndexer";

    /// <summary>
    /// Service version for telemetry.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// ActivitySource for distributed tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    /// <summary>
    /// Meter for metrics collection.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    #region Counters

    /// <summary>
    /// Count of memory store operations.
    /// </summary>
    public static readonly Counter<long> MemoryOperations = Meter.CreateCounter<long>(
        "memory_indexer.operations",
        unit: "{operation}",
        description: "Total number of memory operations");

    /// <summary>
    /// Count of memory recall operations.
    /// </summary>
    public static readonly Counter<long> RecallOperations = Meter.CreateCounter<long>(
        "memory_indexer.recalls",
        unit: "{recall}",
        description: "Total number of memory recall operations");

    /// <summary>
    /// Count of embedding generation operations.
    /// </summary>
    public static readonly Counter<long> EmbeddingOperations = Meter.CreateCounter<long>(
        "memory_indexer.embeddings",
        unit: "{embedding}",
        description: "Total number of embedding generation operations");

    /// <summary>
    /// Count of embedding cache hits.
    /// </summary>
    public static readonly Counter<long> EmbeddingCacheHits = Meter.CreateCounter<long>(
        "memory_indexer.embedding_cache_hits",
        unit: "{hit}",
        description: "Total number of embedding cache hits");

    /// <summary>
    /// Count of PII detection operations.
    /// </summary>
    public static readonly Counter<long> PiiDetections = Meter.CreateCounter<long>(
        "memory_indexer.pii_detections",
        unit: "{detection}",
        description: "Total number of PII detection operations");

    /// <summary>
    /// Count of prompt injection detections.
    /// </summary>
    public static readonly Counter<long> InjectionDetections = Meter.CreateCounter<long>(
        "memory_indexer.injection_detections",
        unit: "{detection}",
        description: "Total number of prompt injection detection operations");

    /// <summary>
    /// Count of errors.
    /// </summary>
    public static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "memory_indexer.errors",
        unit: "{error}",
        description: "Total number of errors");

    #endregion

    #region Histograms

    /// <summary>
    /// Duration of memory store operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> StoreLatency = Meter.CreateHistogram<double>(
        "memory_indexer.store_latency",
        unit: "ms",
        description: "Duration of memory store operations");

    /// <summary>
    /// Duration of memory recall operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> RecallLatency = Meter.CreateHistogram<double>(
        "memory_indexer.recall_latency",
        unit: "ms",
        description: "Duration of memory recall operations");

    /// <summary>
    /// Duration of embedding generation in milliseconds.
    /// </summary>
    public static readonly Histogram<double> EmbeddingLatency = Meter.CreateHistogram<double>(
        "memory_indexer.embedding_latency",
        unit: "ms",
        description: "Duration of embedding generation");

    /// <summary>
    /// Vector similarity scores for recall operations.
    /// </summary>
    public static readonly Histogram<double> SimilarityScores = Meter.CreateHistogram<double>(
        "memory_indexer.similarity_scores",
        unit: "{score}",
        description: "Vector similarity scores for recall operations");

    #endregion

    #region Gauges

    private static int _activeOperations;
    private static long _totalMemoriesStored;
    private static long _totalBytesProcessed;

    /// <summary>
    /// Observable gauge for active operations.
    /// </summary>
    public static readonly ObservableGauge<int> ActiveOperations = Meter.CreateObservableGauge(
        "memory_indexer.active_operations",
        () => _activeOperations,
        unit: "{operation}",
        description: "Number of currently active operations");

    /// <summary>
    /// Observable gauge for total memories stored.
    /// </summary>
    public static readonly ObservableGauge<long> TotalMemoriesStored = Meter.CreateObservableGauge(
        "memory_indexer.total_memories",
        () => _totalMemoriesStored,
        unit: "{memory}",
        description: "Total number of memories currently stored");

    /// <summary>
    /// Observable gauge for total bytes processed.
    /// </summary>
    public static readonly ObservableGauge<long> TotalBytesProcessed = Meter.CreateObservableGauge(
        "memory_indexer.bytes_processed",
        unit: "By",
        observeValue: () => _totalBytesProcessed,
        description: "Total bytes processed");

    #endregion

    #region Helper Methods

    /// <summary>
    /// Start an operation activity with standard tags.
    /// </summary>
    public static Activity? StartOperation(string operationName, string? operationType = null)
    {
        Interlocked.Increment(ref _activeOperations);

        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null)
        {
            activity.SetTag("memory_indexer.operation", operationName);
            if (operationType != null)
            {
                activity.SetTag("memory_indexer.operation_type", operationType);
            }
        }
        return activity;
    }

    /// <summary>
    /// Complete an operation and record metrics.
    /// </summary>
    public static void CompleteOperation(Activity? activity, bool success = true, Exception? exception = null)
    {
        Interlocked.Decrement(ref _activeOperations);

        if (activity != null)
        {
            activity.SetTag("memory_indexer.success", success);
            if (exception != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.StackTrace ?? "" }
                }));
                Errors.Add(1, new KeyValuePair<string, object?>("exception_type", exception.GetType().Name));
            }
            else if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
        }
    }

    /// <summary>
    /// Record a store operation.
    /// </summary>
    public static void RecordStoreOperation(double latencyMs, int memoryCount = 1, long bytesProcessed = 0)
    {
        MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "store"));
        StoreLatency.Record(latencyMs);
        Interlocked.Add(ref _totalMemoriesStored, memoryCount);
        Interlocked.Add(ref _totalBytesProcessed, bytesProcessed);
    }

    /// <summary>
    /// Record a recall operation.
    /// </summary>
    public static void RecordRecallOperation(double latencyMs, int resultCount = 0, double? topScore = null)
    {
        RecallOperations.Add(1);
        RecallLatency.Record(latencyMs);
        if (topScore.HasValue)
        {
            SimilarityScores.Record(topScore.Value);
        }
    }

    /// <summary>
    /// Record an embedding operation.
    /// </summary>
    public static void RecordEmbeddingOperation(double latencyMs, bool cacheHit = false)
    {
        EmbeddingOperations.Add(1);
        EmbeddingLatency.Record(latencyMs);
        if (cacheHit)
        {
            EmbeddingCacheHits.Add(1);
        }
    }

    /// <summary>
    /// Record a PII detection operation.
    /// </summary>
    public static void RecordPiiDetection(int entitiesFound)
    {
        PiiDetections.Add(1, new KeyValuePair<string, object?>("entities_found", entitiesFound));
    }

    /// <summary>
    /// Record an injection detection operation.
    /// </summary>
    public static void RecordInjectionDetection(bool detected, string riskLevel)
    {
        InjectionDetections.Add(1,
            new KeyValuePair<string, object?>("detected", detected),
            new KeyValuePair<string, object?>("risk_level", riskLevel));
    }

    /// <summary>
    /// Update the total memories count (for gauges).
    /// </summary>
    public static void SetTotalMemories(long count)
    {
        Interlocked.Exchange(ref _totalMemoriesStored, count);
    }

    #endregion
}
