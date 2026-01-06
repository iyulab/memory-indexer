namespace MemoryIndexer.Models;

/// <summary>
/// Latency metrics for recall operations across memory tiers.
/// Phase 22.2: Recall Latency Optimization
/// </summary>
public sealed class LatencyMetrics
{
    /// <summary>
    /// User ID this metric is associated with
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Memory tier (Working, Session, User)
    /// </summary>
    public required string Tier { get; init; }

    /// <summary>
    /// Total queries executed
    /// </summary>
    public int TotalQueries { get; set; }

    /// <summary>
    /// Average latency in milliseconds
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Minimum latency in milliseconds
    /// </summary>
    public double MinLatencyMs { get; set; }

    /// <summary>
    /// Maximum latency in milliseconds
    /// </summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// P50 (median) latency in milliseconds
    /// </summary>
    public double P50LatencyMs { get; set; }

    /// <summary>
    /// P95 latency in milliseconds
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// P99 latency in milliseconds
    /// </summary>
    public double P99LatencyMs { get; set; }

    /// <summary>
    /// Latency variance (spread)
    /// </summary>
    public double VarianceMs { get; set; }

    /// <summary>
    /// Cache hit rate (0.0-1.0)
    /// </summary>
    public double CacheHitRate { get; set; }

    /// <summary>
    /// Embedding cache hits
    /// </summary>
    public int EmbeddingCacheHits { get; set; }

    /// <summary>
    /// Embedding cache misses
    /// </summary>
    public int EmbeddingCacheMisses { get; set; }

    /// <summary>
    /// Query result cache hits
    /// </summary>
    public int QueryCacheHits { get; set; }

    /// <summary>
    /// Query result cache misses
    /// </summary>
    public int QueryCacheMisses { get; set; }

    /// <summary>
    /// Number of times latency budget was exceeded
    /// </summary>
    public int BudgetExceededCount { get; set; }

    /// <summary>
    /// Latency budget for this tier in milliseconds
    /// </summary>
    public double LatencyBudgetMs { get; set; }

    /// <summary>
    /// Budget compliance rate (0.0-1.0)
    /// </summary>
    public double BudgetComplianceRate => TotalQueries > 0
        ? 1.0 - ((double)BudgetExceededCount / TotalQueries)
        : 1.0;

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Breakdown of latency by component (embedding, search, reranking)
    /// </summary>
    public Dictionary<string, double> ComponentLatencies { get; set; } = new();
}
