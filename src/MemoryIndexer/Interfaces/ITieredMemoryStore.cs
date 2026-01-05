using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Unified interface for tiered memory storage (L2: Session, L3: User).
/// Extends base IMemoryStore with tier-aware operations.
/// </summary>
/// <remarks>
/// Research reference: research-03.md Section "계층적 저장소 아키텍처"
/// - L2 Session: Vector DB (Qdrant/SQLite-vec), session-scoped
/// - L3 User: Hybrid (Vector + Graph DB), cross-session persistent
/// </remarks>
public interface ITieredMemoryStore : IMemoryStore
{
    /// <summary>
    /// Stores a memory at a specific tier.
    /// </summary>
    /// <param name="memory">The memory to store.</param>
    /// <param name="tier">Target storage tier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memory with updated tier.</returns>
    Task<MemoryUnit> StoreAtTierAsync(
        MemoryUnit memory,
        MemoryTier tier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes a memory to a higher tier.
    /// L3 (User) → L2 (Session) → L1 (Working, handled by IWorkingMemory)
    /// </summary>
    /// <param name="memoryId">The memory ID to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The promoted memory, or null if not found or already at highest tier.</returns>
    Task<MemoryUnit?> PromoteAsync(Guid memoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Demotes a memory to a lower tier.
    /// L1 (Working) → L2 (Session) → L3 (User)
    /// </summary>
    /// <param name="memory">The memory to demote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The demoted memory.</returns>
    Task<MemoryUnit> DemoteAsync(MemoryUnit memory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memories by tier.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tier">The memory tier.</param>
    /// <param name="options">Filter options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memories at the specified tier.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetByTierAsync(
        string userId,
        MemoryTier tier,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memories that are candidates for consolidation (promotion to higher stability).
    /// Based on access patterns and retention scores.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tier">Source tier to check.</param>
    /// <param name="limit">Maximum candidates to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidation candidates ordered by priority.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetConsolidationCandidatesAsync(
        string userId,
        MemoryTier tier,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memories that are candidates for eviction (low retention, not locked).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tier">Source tier to check.</param>
    /// <param name="retentionThreshold">Minimum retention score to avoid eviction.</param>
    /// <param name="limit">Maximum candidates to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Eviction candidates ordered by priority (lowest retention first).</returns>
    Task<IReadOnlyList<MemoryUnit>> GetEvictionCandidatesAsync(
        string userId,
        MemoryTier tier,
        float retentionThreshold = 0.1f,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates session memories to user tier on session end.
    /// Applies retention-based filtering.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID to migrate.</param>
    /// <param name="retentionThreshold">Minimum retention to migrate (others are discarded).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Migration result with counts.</returns>
    Task<TierMigrationResult> MigrateSessionToUserAsync(
        string userId,
        string sessionId,
        float retentionThreshold = 0.3f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets storage statistics by tier.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics for each tier.</returns>
    Task<TierStorageStatistics> GetStatisticsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a tier migration operation.
/// </summary>
public sealed class TierMigrationResult
{
    /// <summary>
    /// Number of memories migrated.
    /// </summary>
    public int MigratedCount { get; init; }

    /// <summary>
    /// Number of memories discarded (below retention threshold).
    /// </summary>
    public int DiscardedCount { get; init; }

    /// <summary>
    /// Number of memories that were already at target tier.
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// IDs of migrated memories.
    /// </summary>
    public IReadOnlyList<Guid> MigratedIds { get; init; } = [];
}

/// <summary>
/// Storage statistics by tier.
/// </summary>
public sealed class TierStorageStatistics
{
    /// <summary>
    /// Statistics for Working Memory (L1).
    /// </summary>
    public TierStatistics Working { get; init; } = new();

    /// <summary>
    /// Statistics for Session Memory (L2).
    /// </summary>
    public TierStatistics Session { get; init; } = new();

    /// <summary>
    /// Statistics for User Memory (L3).
    /// </summary>
    public TierStatistics User { get; init; } = new();

    /// <summary>
    /// Total across all tiers.
    /// </summary>
    public TierStatistics Total => new()
    {
        Count = Working.Count + Session.Count + User.Count,
        AverageRetention = (Working.Count * Working.AverageRetention +
                           Session.Count * Session.AverageRetention +
                           User.Count * User.AverageRetention) /
                          Math.Max(1, Working.Count + Session.Count + User.Count),
        LockedCount = Working.LockedCount + Session.LockedCount + User.LockedCount,
        EstimatedTokens = Working.EstimatedTokens + Session.EstimatedTokens + User.EstimatedTokens
    };
}

/// <summary>
/// Statistics for a single tier.
/// </summary>
public sealed class TierStatistics
{
    /// <summary>
    /// Number of memories in this tier.
    /// </summary>
    public long Count { get; init; }

    /// <summary>
    /// Average retention score.
    /// </summary>
    public float AverageRetention { get; init; }

    /// <summary>
    /// Number of locked (non-evictable) memories.
    /// </summary>
    public long LockedCount { get; init; }

    /// <summary>
    /// Estimated token count for this tier.
    /// </summary>
    public long EstimatedTokens { get; init; }

    /// <summary>
    /// Distribution by memory type.
    /// </summary>
    public Dictionary<MemoryType, long> TypeDistribution { get; init; } = [];

    /// <summary>
    /// Distribution by stability level.
    /// </summary>
    public Dictionary<MemoryStability, long> StabilityDistribution { get; init; } = [];
}
