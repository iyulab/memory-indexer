using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Monitors memory growth rate and provides growth metrics.
/// Phase 22.1: Memory Growth Rate Control.
/// </summary>
public interface IMemoryGrowthMonitor
{
    /// <summary>
    /// Gets current growth metrics for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Growth metrics.</returns>
    Task<MemoryGrowthMetrics> GetGrowthMetricsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory storage attempt (successful or filtered).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="filtered">Whether the memory was filtered (not stored).</param>
    /// <param name="reason">Reason for filtering (if filtered).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordMemoryStorageAsync(string userId, bool filtered, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the end of a round for growth rate calculation.
    /// A "round" is a logical grouping of operations (e.g., one Q&A cycle).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EndRoundAsync(string userId, CancellationToken cancellationToken = default);
}
