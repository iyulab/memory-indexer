using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for balancing memory type distribution through adaptive weighting.
/// </summary>
/// <remarks>
/// Phase 23.1: Memory Type Distribution Balancing.
///
/// Provides boost factors for underrepresented memory types to achieve
/// target distribution goals (e.g., Episodic ~40%, Semantic ~30%,
/// Procedural ~20%, Fact ~10%).
///
/// Boost calculation:
/// - If current &lt; target: Apply positive boost
/// - If current > target: No boost (or negative adjustment)
/// - Boost = (target - current) * sensitivity, clamped to [0, maxBoost]
/// </remarks>
public interface IMemoryTypeBalancer
{
    /// <summary>
    /// Calculate boost factor for a specific memory type for a user.
    /// </summary>
    /// <param name="type">The memory type to check.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Boost factor (0.0 - maxBoost).
    /// 0.0 = no boost (type at or above target distribution)
    /// > 0.0 = positive boost (type underrepresented)
    /// </returns>
    Task<float> GetTypeBoostAsync(
        MemoryType type,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current memory type distribution for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping MemoryType to percentage (0.0 - 1.0).
    /// Percentages sum to 1.0.
    /// </returns>
    Task<IReadOnlyDictionary<MemoryType, float>> GetTypeDistributionAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current memory counts per type for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping MemoryType to count.</returns>
    Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
