using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Latency profiling and tracking for memory recall operations.
/// Phase 22.2: Recall Latency Optimization
/// </summary>
public interface ILatencyProfiler
{
    /// <summary>
    /// Record a recall operation latency
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="tier">Memory tier (Working, Session, User)</param>
    /// <param name="latencyMs">Latency in milliseconds</param>
    /// <param name="componentLatencies">Optional breakdown by component</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordLatencyAsync(
        string userId,
        string tier,
        double latencyMs,
        Dictionary<string, double>? componentLatencies = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record cache hit or miss
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cacheType">Cache type (Embedding, Query)</param>
    /// <param name="hit">True for hit, false for miss</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordCacheAccessAsync(
        string userId,
        string cacheType,
        bool hit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get latency metrics for a user and tier
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="tier">Optional tier filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latency metrics</returns>
    Task<IReadOnlyList<LatencyMetrics>> GetMetricsAsync(
        string userId,
        string? tier = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset metrics for a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ResetMetricsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get latency budget for a tier
    /// </summary>
    /// <param name="tier">Tier name</param>
    /// <returns>Latency budget in milliseconds</returns>
    double GetLatencyBudget(string tier);
}
