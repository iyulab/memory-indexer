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
    /// Count of query cache hits (Phase v0.5.0).
    /// </summary>
    public static readonly Counter<long> QueryCacheHits = Meter.CreateCounter<long>(
        "memory_indexer.query_cache_hits",
        unit: "{hit}",
        description: "Total number of query result cache hits");

    /// <summary>
    /// Count of duplicate recall queries detected (Phase v0.5.0).
    /// </summary>
    public static readonly Counter<long> DuplicateRecalls = Meter.CreateCounter<long>(
        "memory_indexer.duplicate_recalls",
        unit: "{recall}",
        description: "Total number of duplicate recall queries detected");

    /// <summary>
    /// Count of rapid-fire recall patterns detected (Phase v0.5.0).
    /// </summary>
    public static readonly Counter<long> RapidFireRecalls = Meter.CreateCounter<long>(
        "memory_indexer.rapid_fire_recalls",
        unit: "{pattern}",
        description: "Total number of rapid-fire recall patterns detected");

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

    #region Intelligence Counters (Phase v0.5.0)

    /// <summary>
    /// Count of memory classification operations.
    /// </summary>
    public static readonly Counter<long> ClassificationOperations = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.classifications",
        unit: "{classification}",
        description: "Total number of memory classification operations");

    /// <summary>
    /// Count of summarization operations.
    /// </summary>
    public static readonly Counter<long> SummarizationOperations = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.summarizations",
        unit: "{summarization}",
        description: "Total number of summarization operations");

    /// <summary>
    /// Count of deduplication operations.
    /// </summary>
    public static readonly Counter<long> DeduplicationOperations = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.deduplications",
        unit: "{deduplication}",
        description: "Total number of deduplication operations");

    /// <summary>
    /// Count of conflict detection operations.
    /// </summary>
    public static readonly Counter<long> ConflictDetections = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.conflict_detections",
        unit: "{detection}",
        description: "Total number of conflict detection operations");

    /// <summary>
    /// Count of entity extraction operations.
    /// </summary>
    public static readonly Counter<long> EntityExtractions = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.entity_extractions",
        unit: "{extraction}",
        description: "Total number of entity extraction operations");

    /// <summary>
    /// Count of reranking operations.
    /// </summary>
    public static readonly Counter<long> RerankingOperations = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.rerankings",
        unit: "{reranking}",
        description: "Total number of reranking operations");

    /// <summary>
    /// Count of tier promotion events.
    /// </summary>
    public static readonly Counter<long> TierPromotions = Meter.CreateCounter<long>(
        "memory_indexer.tier_promotions",
        unit: "{promotion}",
        description: "Total number of tier promotion events");

    /// <summary>
    /// Count of token budget warnings.
    /// </summary>
    public static readonly Counter<long> TokenBudgetWarnings = Meter.CreateCounter<long>(
        "memory_indexer.token_budget_warnings",
        unit: "{warning}",
        description: "Total number of token budget warning events");

    /// <summary>
    /// Count of token budget exceeded events.
    /// </summary>
    public static readonly Counter<long> TokenBudgetExceeded = Meter.CreateCounter<long>(
        "memory_indexer.token_budget_exceeded",
        unit: "{exceeded}",
        description: "Total number of token budget exceeded events");

    /// <summary>
    /// Count of knowledge graph queries.
    /// </summary>
    public static readonly Counter<long> GraphQueries = Meter.CreateCounter<long>(
        "memory_indexer.intelligence.graph_queries",
        unit: "{query}",
        description: "Total number of knowledge graph queries");

    /// <summary>
    /// Count of resource limit exceeded events.
    /// Phase v0.6.0-γ: Resource Management.
    /// </summary>
    public static readonly Counter<long> ResourceLimitExceeded = Meter.CreateCounter<long>(
        "memory_indexer.resource_limit_exceeded",
        unit: "{exceeded}",
        description: "Total number of resource limit exceeded events");

    /// <summary>
    /// Count of resource warning events.
    /// Phase v0.6.0-γ: Resource Management.
    /// </summary>
    public static readonly Counter<long> ResourceWarnings = Meter.CreateCounter<long>(
        "memory_indexer.resource_warnings",
        unit: "{warning}",
        description: "Total number of resource usage warning events");

    #endregion

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

    #region Intelligence Histograms (Phase v0.5.0)

    /// <summary>
    /// Duration of classification operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> ClassificationLatency = Meter.CreateHistogram<double>(
        "memory_indexer.intelligence.classification_latency",
        unit: "ms",
        description: "Duration of memory classification operations");

    /// <summary>
    /// Duration of summarization operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> SummarizationLatency = Meter.CreateHistogram<double>(
        "memory_indexer.intelligence.summarization_latency",
        unit: "ms",
        description: "Duration of summarization operations");

    /// <summary>
    /// Duration of deduplication operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> DeduplicationLatency = Meter.CreateHistogram<double>(
        "memory_indexer.intelligence.deduplication_latency",
        unit: "ms",
        description: "Duration of deduplication operations");

    /// <summary>
    /// Duration of reranking operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> RerankingLatency = Meter.CreateHistogram<double>(
        "memory_indexer.intelligence.reranking_latency",
        unit: "ms",
        description: "Duration of reranking operations");

    /// <summary>
    /// Duration of graph query operations in milliseconds.
    /// </summary>
    public static readonly Histogram<double> GraphQueryLatency = Meter.CreateHistogram<double>(
        "memory_indexer.intelligence.graph_query_latency",
        unit: "ms",
        description: "Duration of knowledge graph query operations");

    /// <summary>
    /// Token budget usage ratio per session.
    /// </summary>
    public static readonly Histogram<double> TokenBudgetUsage = Meter.CreateHistogram<double>(
        "memory_indexer.token_budget_usage",
        unit: "{ratio}",
        description: "Token budget usage ratio per session");

    #endregion

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
    /// Record a query cache hit (Phase v0.5.0).
    /// </summary>
    public static void RecordQueryCacheHit(string userId, string tier)
    {
        QueryCacheHits.Add(1,
            new KeyValuePair<string, object?>("user_id", userId),
            new KeyValuePair<string, object?>("tier", tier));
    }

    /// <summary>
    /// Record a duplicate recall pattern (Phase v0.5.0).
    /// </summary>
    public static void RecordDuplicateRecall(string userId, int duplicateCount)
    {
        DuplicateRecalls.Add(1,
            new KeyValuePair<string, object?>("user_id", userId),
            new KeyValuePair<string, object?>("duplicate_count", duplicateCount));
    }

    /// <summary>
    /// Record a rapid-fire recall pattern (Phase v0.5.0).
    /// </summary>
    public static void RecordRapidFireRecall(string userId, int recallsInWindow)
    {
        RapidFireRecalls.Add(1,
            new KeyValuePair<string, object?>("user_id", userId),
            new KeyValuePair<string, object?>("recalls_in_window", recallsInWindow));
    }

    /// <summary>
    /// Update the total memories count (for gauges).
    /// </summary>
    public static void SetTotalMemories(long count)
    {
        Interlocked.Exchange(ref _totalMemoriesStored, count);
    }

    #region Intelligence Recording Methods (Phase v0.5.0)

    /// <summary>
    /// Record a classification operation.
    /// </summary>
    public static void RecordClassificationOperation(double latencyMs, string classifiedType)
    {
        ClassificationOperations.Add(1,
            new KeyValuePair<string, object?>("classified_type", classifiedType));
        ClassificationLatency.Record(latencyMs);
    }

    /// <summary>
    /// Record a summarization operation.
    /// </summary>
    public static void RecordSummarizationOperation(double latencyMs, int inputTokens, int outputTokens)
    {
        SummarizationOperations.Add(1,
            new KeyValuePair<string, object?>("input_tokens", inputTokens),
            new KeyValuePair<string, object?>("output_tokens", outputTokens));
        SummarizationLatency.Record(latencyMs);
    }

    /// <summary>
    /// Record a deduplication operation.
    /// </summary>
    public static void RecordDeduplicationOperation(double latencyMs, string action, double similarity)
    {
        DeduplicationOperations.Add(1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("similarity", similarity));
        DeduplicationLatency.Record(latencyMs);
    }

    /// <summary>
    /// Record a conflict detection operation.
    /// </summary>
    public static void RecordConflictDetection(bool conflictFound, string? conflictType = null)
    {
        ConflictDetections.Add(1,
            new KeyValuePair<string, object?>("conflict_found", conflictFound),
            new KeyValuePair<string, object?>("conflict_type", conflictType ?? "none"));
    }

    /// <summary>
    /// Record an entity extraction operation.
    /// </summary>
    public static void RecordEntityExtraction(int entitiesExtracted, int relationsExtracted)
    {
        EntityExtractions.Add(1,
            new KeyValuePair<string, object?>("entities_extracted", entitiesExtracted),
            new KeyValuePair<string, object?>("relations_extracted", relationsExtracted));
    }

    /// <summary>
    /// Record a reranking operation.
    /// </summary>
    public static void RecordRerankingOperation(double latencyMs, int candidateCount, int resultCount)
    {
        RerankingOperations.Add(1,
            new KeyValuePair<string, object?>("candidate_count", candidateCount),
            new KeyValuePair<string, object?>("result_count", resultCount));
        RerankingLatency.Record(latencyMs);
    }

    /// <summary>
    /// Record a tier promotion event.
    /// </summary>
    public static void RecordTierPromotion(string sourceTier, string targetTier, int memoryCount)
    {
        TierPromotions.Add(memoryCount,
            new KeyValuePair<string, object?>("source_tier", sourceTier),
            new KeyValuePair<string, object?>("target_tier", targetTier));
    }

    /// <summary>
    /// Record a token budget warning event.
    /// </summary>
    public static void RecordTokenBudgetWarning(string sessionId, float usageRatio)
    {
        TokenBudgetWarnings.Add(1,
            new KeyValuePair<string, object?>("session_id", sessionId),
            new KeyValuePair<string, object?>("usage_ratio", usageRatio));
        TokenBudgetUsage.Record(usageRatio);
    }

    /// <summary>
    /// Record a token budget exceeded event.
    /// </summary>
    public static void RecordTokenBudgetExceeded(string sessionId, float usageRatio)
    {
        TokenBudgetExceeded.Add(1,
            new KeyValuePair<string, object?>("session_id", sessionId),
            new KeyValuePair<string, object?>("usage_ratio", usageRatio));
        TokenBudgetUsage.Record(usageRatio);
    }

    /// <summary>
    /// Record a knowledge graph query.
    /// </summary>
    public static void RecordGraphQuery(double latencyMs, string queryType, int resultCount)
    {
        GraphQueries.Add(1,
            new KeyValuePair<string, object?>("query_type", queryType),
            new KeyValuePair<string, object?>("result_count", resultCount));
        GraphQueryLatency.Record(latencyMs);
    }

    /// <summary>
    /// Record a resource limit exceeded event.
    /// Phase v0.6.0-γ: Resource Management.
    /// </summary>
    public static void RecordResourceLimitExceeded(string userId, string? tenantId, string limitType, long current, long limit)
    {
        ResourceLimitExceeded.Add(1,
            new KeyValuePair<string, object?>("user_id", userId),
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("limit_type", limitType),
            new KeyValuePair<string, object?>("current_value", current),
            new KeyValuePair<string, object?>("limit_value", limit));
    }

    /// <summary>
    /// Record a resource warning event.
    /// Phase v0.6.0-γ: Resource Management.
    /// </summary>
    public static void RecordResourceWarning(string userId, string? tenantId, string resourceType, double usagePercent)
    {
        ResourceWarnings.Add(1,
            new KeyValuePair<string, object?>("user_id", userId),
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("resource_type", resourceType),
            new KeyValuePair<string, object?>("usage_percent", usagePercent));
    }

    #endregion

    #endregion
}

