using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Interface for promoting Recently buffer items to Working memory.
/// Acts as the bridge between Tier 0 (Buffer) and Tier 1 (Working).
/// </summary>
/// <remarks>
/// 4-Tier Architecture:
/// - Recently (Buffer): Raw conversation staging
/// - Working (L1): Topic-grouped, summarized active context - THIS TRANSITION
/// - Session (L2): Archived session summaries
/// - User (L3): Profile dictionary
///
/// Promotion pipeline:
/// 1. Drain items from RecentlyBuffer
/// 2. Group by topic using TopicSegmenter
/// 3. Create MemoryUnits per topic group
/// 4. Promote to WorkingMemory
/// </remarks>
public interface IBufferPromoter
{
    /// <summary>
    /// Promotes items from the Recently buffer to Working memory for a user.
    /// Applies topic grouping and creates MemoryUnits.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="trigger">The trigger that caused the promotion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing created memories and any evicted items.</returns>
    Task<BufferPromotionResult> PromoteAsync(
        string userId,
        PromotionTriggerType trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes specific items from the Recently buffer.
    /// Used for manual or partial promotion.
    /// </summary>
    /// <param name="items">The items to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing created memories and any evicted items.</returns>
    Task<BufferPromotionResult> PromoteItemsAsync(
        IReadOnlyList<RecentlyMemory> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any user has pending items that should be promoted.
    /// Used by background worker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of user IDs with satisfied promotion triggers.</returns>
    Task<IReadOnlyList<UserPromotionCheck>> CheckPendingPromotionsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a buffer promotion operation.
/// </summary>
public sealed record BufferPromotionResult
{
    /// <summary>
    /// Whether the promotion succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The trigger that caused the promotion.
    /// </summary>
    public PromotionTriggerType Trigger { get; init; }

    /// <summary>
    /// Number of buffer items processed.
    /// </summary>
    public int ItemsProcessed { get; init; }

    /// <summary>
    /// Number of topic groups created.
    /// </summary>
    public int TopicGroupsCreated { get; init; }

    /// <summary>
    /// Memories created and promoted to Working tier.
    /// </summary>
    public IReadOnlyList<MemoryUnit> CreatedMemories { get; init; } = [];

    /// <summary>
    /// Memories evicted from Working tier to make room.
    /// These should be demoted to Session tier.
    /// </summary>
    public IReadOnlyList<MemoryUnit> EvictedMemories { get; init; } = [];

    /// <summary>
    /// Error message if promotion failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Duration of the promotion operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Empty successful result.
    /// </summary>
    public static BufferPromotionResult Empty => new()
    {
        Success = true,
        Trigger = PromotionTriggerType.None,
        ItemsProcessed = 0,
        TopicGroupsCreated = 0
    };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static BufferPromotionResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Result of checking a user's buffer for pending promotions.
/// </summary>
public sealed class UserPromotionCheck
{
    /// <summary>
    /// The user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The trigger type that is satisfied.
    /// </summary>
    public required PromotionTriggerType Trigger { get; init; }

    /// <summary>
    /// Number of pending items.
    /// </summary>
    public int PendingItems { get; init; }

    /// <summary>
    /// Total tokens pending.
    /// </summary>
    public int PendingTokens { get; init; }
}
