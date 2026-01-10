namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Service for collecting and computing evaluation metrics.
/// Provides standardized KPIs for memory system assessment.
/// </summary>
public interface IEvaluationService
{
    /// <summary>
    /// Collects current evaluation metrics for a user/session.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="sessionId">Optional session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current evaluation metrics.</returns>
    Task<EvaluationMetrics> CollectMetricsAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes Context Compression Ratio for a recall operation.
    /// </summary>
    /// <param name="recalledTokens">Number of tokens in recalled content.</param>
    /// <param name="fullHistoryTokens">Total tokens in full conversation history.</param>
    /// <returns>CCR value (lower is better).</returns>
    double ComputeCcr(long recalledTokens, long fullHistoryTokens);

    /// <summary>
    /// Computes Recall@K efficiency for a retrieval operation.
    /// </summary>
    /// <param name="relevantInTopK">Number of relevant items in top K results.</param>
    /// <param name="k">Number of results retrieved.</param>
    /// <returns>Recall@K efficiency (higher is better).</returns>
    double ComputeRecallAtK(int relevantInTopK, int k);

    /// <summary>
    /// Records tier promotion latency.
    /// </summary>
    /// <param name="fromTier">Source tier name.</param>
    /// <param name="toTier">Destination tier name.</param>
    /// <param name="latencyMs">Latency in milliseconds.</param>
    void RecordTierPromotionLatency(string fromTier, string toTier, double latencyMs);

    /// <summary>
    /// Computes Information Retention Score.
    /// </summary>
    /// <param name="correctlyRecalled">Number of correctly recalled items.</param>
    /// <param name="totalStored">Total items that were stored.</param>
    /// <returns>Retention score (higher is better).</returns>
    double ComputeRetentionScore(int correctlyRecalled, int totalStored);

    /// <summary>
    /// Gets cognitive compliance metrics for tier distribution.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="sessionId">Optional session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cognitive compliance metrics.</returns>
    Task<CognitiveComplianceMetrics> GetCognitiveComplianceAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates evaluation report for a session.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="sessionId">Optional session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted evaluation report.</returns>
    Task<EvaluationReport> GenerateReportAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Comprehensive evaluation report combining all metrics.
/// </summary>
public record EvaluationReport
{
    /// <summary>
    /// Core KPI metrics.
    /// </summary>
    public EvaluationMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Cognitive compliance metrics.
    /// </summary>
    public CognitiveComplianceMetrics CognitiveCompliance { get; init; } = new();

    /// <summary>
    /// Overall score (0-100).
    /// </summary>
    public double OverallScore { get; init; }

    /// <summary>
    /// Report generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Evaluation period start.
    /// </summary>
    public DateTimeOffset? PeriodStart { get; init; }

    /// <summary>
    /// Evaluation period end.
    /// </summary>
    public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>
    /// Summary observations and recommendations.
    /// </summary>
    public IReadOnlyList<string> Observations { get; init; } = [];
}
