namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Core KPI metrics for memory system evaluation.
/// Based on cognitive science-backed evaluation framework.
/// </summary>
public record EvaluationMetrics
{
    /// <summary>
    /// Context Compression Ratio: recalled_tokens / full_history_tokens.
    /// Lower is better - indicates efficient context management.
    /// Target: &lt;1% of full context for NIAH tests.
    /// </summary>
    public double ContextCompressionRatio { get; init; }

    /// <summary>
    /// Recall@K Efficiency: Precision of top-K retrieval.
    /// Higher is better - indicates accurate semantic retrieval.
    /// Measured as: relevant_in_top_k / k
    /// </summary>
    public double RecallAtKEfficiency { get; init; }

    /// <summary>
    /// K value used for Recall@K calculation.
    /// </summary>
    public int RecallK { get; init; } = 5;

    /// <summary>
    /// Tier Promotion Latency in milliseconds.
    /// Buffer → Short → Long transition time.
    /// Lower is better for real-time service suitability.
    /// </summary>
    public TierPromotionLatency TierPromotionLatency { get; init; } = new();

    /// <summary>
    /// Information Retention Score: Long-term recall accuracy.
    /// Higher is better - proves superiority over sliding window.
    /// Measured as: correctly_recalled / total_stored over time.
    /// </summary>
    public double InformationRetentionScore { get; init; }

    /// <summary>
    /// Timestamp when metrics were collected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// User ID for which metrics were collected.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID for which metrics were collected.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Total tokens in full conversation history.
    /// </summary>
    public long FullHistoryTokens { get; init; }

    /// <summary>
    /// Tokens actually recalled/used.
    /// </summary>
    public long RecalledTokens { get; init; }

    /// <summary>
    /// Number of memories stored during evaluation.
    /// </summary>
    public int MemoriesStored { get; init; }

    /// <summary>
    /// Number of successful recalls during evaluation.
    /// </summary>
    public int SuccessfulRecalls { get; init; }

    /// <summary>
    /// Total recall attempts during evaluation.
    /// </summary>
    public int TotalRecallAttempts { get; init; }
}

/// <summary>
/// Tier promotion latency breakdown.
/// </summary>
public record TierPromotionLatency
{
    /// <summary>
    /// Buffer (T0) → Short (T1) promotion latency in milliseconds.
    /// </summary>
    public double BufferToShortMs { get; init; }

    /// <summary>
    /// Short (T1) → Long (T2) promotion latency in milliseconds.
    /// </summary>
    public double ShortToLongMs { get; init; }

    /// <summary>
    /// Long (T2) → Archive (T3) promotion latency in milliseconds.
    /// </summary>
    public double LongToArchiveMs { get; init; }

    /// <summary>
    /// Total promotion latency across all tiers.
    /// </summary>
    public double TotalMs => BufferToShortMs + ShortToLongMs + LongToArchiveMs;
}

/// <summary>
/// Cognitive compliance metrics aligned with memory science.
/// </summary>
public record CognitiveComplianceMetrics
{
    /// <summary>
    /// Working Memory (7±2) compliance rate.
    /// Percentage of time Short tier stays within 5-9 items.
    /// Based on Baddeley's working memory model.
    /// </summary>
    public double WorkingMemoryCompliance { get; init; }

    /// <summary>
    /// Current Short tier item count.
    /// </summary>
    public int ShortTierCount { get; init; }

    /// <summary>
    /// Healthy Tier Flow compliance.
    /// Validates: Buffer ≤ 2, Short ≤ 9, Long ≥ 1.
    /// </summary>
    public bool HealthyTierFlow { get; init; }

    /// <summary>
    /// Buffer tier count (should be ≤ 2).
    /// </summary>
    public int BufferCount { get; init; }

    /// <summary>
    /// Long tier count (should be ≥ 1 for active sessions).
    /// </summary>
    public int LongCount { get; init; }

    /// <summary>
    /// Archive tier count.
    /// </summary>
    public int ArchiveCount { get; init; }
}
