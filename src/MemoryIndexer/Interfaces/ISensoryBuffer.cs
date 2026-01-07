using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Tier 0: Sensory Buffer interface.
/// Async staging area for raw sensory input before promotion to Working memory.
/// Implements Atkinson-Shiffrin Multi-Store Model's sensory memory component.
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - SensoryBuffer (T0): Raw sensory input, async staging - THIS TIER
/// - WorkingMemory (T1): Processed active context
/// - EpisodicStore (T2): Archived episodic memories
/// - SemanticStore (T3): Consolidated semantic knowledge
///
/// Multi-signal promotion triggers (OR logic):
/// - IdleTimeout: No activity for specified duration
/// - TokenThreshold: Accumulated tokens exceed threshold
/// - TurnThreshold: Turn count exceeds threshold
/// </remarks>
public interface ISensoryBuffer
{
    /// <summary>
    /// Gets the current number of items in the buffer for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Number of buffered items.</returns>
    int GetCount(string userId);

    /// <summary>
    /// Gets the total accumulated tokens in the buffer for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Total token count.</returns>
    int GetTokenCount(string userId);

    /// <summary>
    /// Enqueues a new item into the Sensory buffer.
    /// </summary>
    /// <param name="content">The raw content to buffer.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">Optional session ID.</param>
    /// <param name="role">Optional role (user, assistant, system).</param>
    /// <param name="metadata">Optional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created buffer item.</returns>
    Task<SensoryMemory> EnqueueAsync(
        string content,
        string userId,
        string? sessionId = null,
        string? role = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pending items in the buffer for a user.
    /// Does not remove items from the buffer.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of buffered items.</returns>
    Task<IReadOnlyList<SensoryMemory>> GetPendingAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any promotion trigger is satisfied for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trigger type if promotion should occur, null otherwise.</returns>
    Task<PromotionTriggerType?> CheckTriggerAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains all items from the buffer for promotion.
    /// Items are removed from the buffer and returned.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All drained items.</returns>
    Task<IReadOnlyList<SensoryMemory>> DrainAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains items up to the specified count for incremental promotion.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="maxItems">Maximum items to drain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Drained items up to the limit.</returns>
    Task<IReadOnlyList<SensoryMemory>> DrainAsync(
        string userId,
        int maxItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all items from the buffer for a user.
    /// Use with caution - items are discarded without promotion.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of discarded items.</returns>
    Task<int> ClearAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the buffer state for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Buffer statistics.</returns>
    SensoryBufferStats GetStats(string userId);

    /// <summary>
    /// Gets all active user IDs with pending buffer items.
    /// Used by background worker to check triggers.
    /// </summary>
    /// <returns>List of user IDs with pending items.</returns>
    IReadOnlyList<string> GetActiveUserIds();
}

/// <summary>
/// Statistics about the Sensory buffer state.
/// </summary>
public sealed class SensoryBufferStats
{
    /// <summary>
    /// Number of items in the buffer.
    /// </summary>
    public int ItemCount { get; init; }

    /// <summary>
    /// Total tokens in the buffer.
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Current turn count.
    /// </summary>
    public int TurnCount { get; init; }

    /// <summary>
    /// Time since last activity.
    /// </summary>
    public TimeSpan? IdleDuration { get; init; }

    /// <summary>
    /// Timestamp of the oldest item in the buffer.
    /// </summary>
    public DateTime? OldestItemTimestamp { get; init; }

    /// <summary>
    /// Timestamp of the newest item in the buffer.
    /// </summary>
    public DateTime? NewestItemTimestamp { get; init; }

    /// <summary>
    /// Whether any trigger is satisfied.
    /// </summary>
    public bool TriggerSatisfied { get; init; }

    /// <summary>
    /// The satisfied trigger type, if any.
    /// </summary>
    public PromotionTriggerType? SatisfiedTrigger { get; init; }

    /// <summary>
    /// Empty stats instance.
    /// </summary>
    public static SensoryBufferStats Empty => new()
    {
        ItemCount = 0,
        TotalTokens = 0,
        TurnCount = 0
    };
}
