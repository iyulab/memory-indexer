using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// Virtual Context Manager (VCM) - The "Operating System" for LLM memory.
/// Manages memory paging between tiers like OS virtual memory management.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 3 "Virtual Context Management"
/// Inspired by MemGPT's virtual context management approach.
///
/// Key concepts:
/// - Page-in: Promote memory from lower to higher tier (L3→L2→L1)
/// - Page-out: Demote memory from higher to lower tier (L1→L2→L3)
/// - Eviction: Remove low-retention memories based on forgetting curve
/// - Consolidation: Merge/summarize related memories
/// </remarks>
public interface IVirtualContextManager
{
    /// <summary>
    /// Gets the current context state.
    /// </summary>
    VirtualContextState State { get; }

    /// <summary>
    /// Initializes VCM for a session.
    /// Loads relevant memories into working memory based on context.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="initialContext">Optional initial context for memory retrieval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(
        string userId,
        string sessionId,
        string? initialContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages in memories relevant to the current query.
    /// Retrieves from lower tiers and promotes to working memory.
    /// </summary>
    /// <param name="query">The query or context to match.</param>
    /// <param name="maxItems">Maximum items to page in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memories paged into working memory.</returns>
    Task<IReadOnlyList<MemoryUnit>> PageInAsync(
        string query,
        int maxItems = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages in a specific memory by ID.
    /// </summary>
    /// <param name="memoryId">The memory ID to page in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The paged-in memory, or null if not found.</returns>
    Task<MemoryUnit?> PageInByIdAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages out memories from working memory to session tier.
    /// Called when working memory is at capacity or context changes.
    /// </summary>
    /// <param name="count">Number of memories to page out (default: 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged out memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> PageOutAsync(
        int count = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs defensive eviction when context saturation is high.
    /// Uses risk-controlled strategy from research-04.md.
    /// </summary>
    /// <param name="targetSaturation">Target saturation level after eviction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Eviction result with affected memories.</returns>
    Task<EvictionResult> DefensiveEvictAsync(
        ContextSaturationLevel targetSaturation = ContextSaturationLevel.Normal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs memory consolidation process.
    /// Identifies and merges related memories, updates stability levels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidation result.</returns>
    Task<ConsolidationResult> ConsolidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates retention scores for all memories based on Ebbinghaus curve.
    /// Should be called periodically.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of memories updated.</returns>
    Task<int> UpdateRetentionScoresAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles session end: consolidates session memories to user tier.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session end result with migration statistics.</returns>
    Task<SessionEndResult> EndSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current context window usage statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Context usage statistics.</returns>
    Task<ContextUsageStatistics> GetContextUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes working memory based on current context and query patterns.
    /// May reorder, evict, or page in memories.
    /// </summary>
    /// <param name="currentContext">Current conversation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OptimizeWorkingMemoryAsync(
        string currentContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Current state of the Virtual Context Manager.
/// </summary>
public sealed class VirtualContextState
{
    /// <summary>
    /// Whether VCM is initialized for a session.
    /// </summary>
    public bool IsInitialized { get; set; }

    /// <summary>
    /// Current user ID.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Current context saturation level.
    /// </summary>
    public ContextSaturationLevel SaturationLevel { get; set; }

    /// <summary>
    /// Estimated token usage in working memory.
    /// </summary>
    public int WorkingMemoryTokens { get; set; }

    /// <summary>
    /// Maximum token capacity for working memory.
    /// </summary>
    public int MaxTokenCapacity { get; set; }

    /// <summary>
    /// Current saturation percentage (0-100).
    /// </summary>
    public float SaturationPercentage => MaxTokenCapacity > 0
        ? (float)WorkingMemoryTokens / MaxTokenCapacity * 100
        : 0;
}

/// <summary>
/// Result of an eviction operation.
/// </summary>
public sealed class EvictionResult
{
    /// <summary>
    /// Number of memories evicted.
    /// </summary>
    public int EvictedCount { get; init; }

    /// <summary>
    /// Number of memories demoted (moved to lower tier).
    /// </summary>
    public int DemotedCount { get; init; }

    /// <summary>
    /// Number of memories summarized (compressed).
    /// </summary>
    public int SummarizedCount { get; init; }

    /// <summary>
    /// Tokens freed by eviction.
    /// </summary>
    public int TokensFreed { get; init; }

    /// <summary>
    /// IDs of affected memories.
    /// </summary>
    public IReadOnlyList<Guid> AffectedIds { get; init; } = [];

    /// <summary>
    /// New saturation level after eviction.
    /// </summary>
    public ContextSaturationLevel NewSaturationLevel { get; init; }
}

/// <summary>
/// Result of a consolidation operation.
/// </summary>
public sealed class ConsolidationResult
{
    /// <summary>
    /// Number of memories merged.
    /// </summary>
    public int MergedCount { get; init; }

    /// <summary>
    /// Number of memories with upgraded stability.
    /// </summary>
    public int StabilityUpgradedCount { get; init; }

    /// <summary>
    /// Number of memories summarized.
    /// </summary>
    public int SummarizedCount { get; init; }

    /// <summary>
    /// Number of memories promoted to higher tier.
    /// </summary>
    public int PromotedCount { get; init; }

    /// <summary>
    /// IDs of new memories created by merging.
    /// </summary>
    public IReadOnlyList<Guid> NewMergedIds { get; init; } = [];
}

/// <summary>
/// Result of session end operation.
/// </summary>
public sealed class SessionEndResult
{
    /// <summary>
    /// Session ID that ended.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Migration result from session to user tier.
    /// </summary>
    public required TierMigrationResult MigrationResult { get; init; }

    /// <summary>
    /// Number of memories discarded (too low retention).
    /// </summary>
    public int DiscardedCount { get; init; }

    /// <summary>
    /// Working memory was cleared.
    /// </summary>
    public int WorkingMemoryClearedCount { get; init; }

    /// <summary>
    /// Duration of the session.
    /// </summary>
    public TimeSpan SessionDuration { get; init; }
}

/// <summary>
/// Context window usage statistics.
/// </summary>
public sealed class ContextUsageStatistics
{
    /// <summary>
    /// Total tokens in use.
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Tokens used by working memory.
    /// </summary>
    public int WorkingMemoryTokens { get; init; }

    /// <summary>
    /// Tokens used by system prompts (locked memories).
    /// </summary>
    public int SystemPromptTokens { get; init; }

    /// <summary>
    /// Available token capacity.
    /// </summary>
    public int AvailableTokens { get; init; }

    /// <summary>
    /// Current saturation level.
    /// </summary>
    public ContextSaturationLevel SaturationLevel { get; init; }

    /// <summary>
    /// Saturation percentage (0-100).
    /// </summary>
    public float SaturationPercentage { get; init; }

    /// <summary>
    /// Number of memories in working memory.
    /// </summary>
    public int WorkingMemoryCount { get; init; }

    /// <summary>
    /// Number of memories in session tier.
    /// </summary>
    public int SessionMemoryCount { get; init; }

    /// <summary>
    /// Number of memories in user tier.
    /// </summary>
    public int UserMemoryCount { get; init; }

    /// <summary>
    /// Recommendation for action based on current state.
    /// </summary>
    public ContextActionRecommendation Recommendation { get; init; }
}

/// <summary>
/// Recommended action based on context usage.
/// </summary>
public enum ContextActionRecommendation
{
    /// <summary>
    /// No action needed, normal operation.
    /// </summary>
    None,

    /// <summary>
    /// Consider summarizing some memories.
    /// </summary>
    ConsiderSummarization,

    /// <summary>
    /// Should page out some working memories.
    /// </summary>
    ShouldPageOut,

    /// <summary>
    /// Immediate eviction required.
    /// </summary>
    ImmediateEvictionRequired,

    /// <summary>
    /// Critical: context window nearly full.
    /// </summary>
    Critical
}
