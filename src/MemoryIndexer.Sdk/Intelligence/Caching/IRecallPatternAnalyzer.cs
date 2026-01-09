namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Interface for analyzing recall patterns to detect inefficient usage.
/// Phase v0.5.0: Recall Pattern Telemetry
/// </summary>
public interface IRecallPatternAnalyzer
{
    /// <summary>
    /// Record a recall operation for pattern analysis.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="query">Query text.</param>
    /// <param name="tier">Memory tier.</param>
    /// <param name="limit">Result limit.</param>
    void RecordRecall(string userId, string query, string tier, int limit);

    /// <summary>
    /// Get recall pattern statistics.
    /// </summary>
    /// <param name="userId">Optional user ID for user-specific stats (null for global).</param>
    /// <returns>Pattern statistics.</returns>
    RecallPatternStatistics GetStatistics(string? userId = null);

    /// <summary>
    /// Get active alerts for problematic patterns.
    /// </summary>
    /// <param name="userId">Optional user ID filter.</param>
    /// <returns>List of alerts.</returns>
    IReadOnlyList<RecallPatternAlert> GetAlerts(string? userId = null);

    /// <summary>
    /// Get optimization recommendations based on observed patterns.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>List of recommendations.</returns>
    IReadOnlyList<RecallOptimizationRecommendation> GetRecommendations(string userId);

    /// <summary>
    /// Reset pattern tracking.
    /// </summary>
    /// <param name="userId">Optional user ID (null resets all).</param>
    void Reset(string? userId = null);
}
