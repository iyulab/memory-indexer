namespace MemoryIndexer.Models;

/// <summary>
/// Represents a raw memory item in the Sensory buffer (Tier 0).
/// This is a staging area for sensory input before promotion to Working memory.
/// Implements Atkinson-Shiffrin Multi-Store Model's sensory register.
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - Buffer (T0): Raw sensory input, async staging - THIS CLASS
/// - Short-Term Memory (T1): Processed active context
/// - LongTermStore (T2): Archived episodic memories
/// - ArchiveStore (T3): Consolidated semantic knowledge
/// </remarks>
public sealed class SensoryMemory
{
    /// <summary>
    /// Unique identifier for this buffer item.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Raw content of the sensory memory.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The user this memory belongs to.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Optional session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// When this item was added to the buffer.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Estimated token count for this content.
    /// Used for token-based promotion trigger.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Turn index within the current buffer sequence.
    /// Used for turn-based promotion trigger.
    /// </summary>
    public int TurnIndex { get; init; }

    /// <summary>
    /// Role of the content (user, assistant, system).
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Optional metadata for the buffer item.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of a promotion operation from Sensory buffer.
/// </summary>
public sealed class PromotionResult
{
    /// <summary>
    /// Whether the promotion was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of items promoted.
    /// </summary>
    public int PromotedCount { get; init; }

    /// <summary>
    /// Total tokens in promoted items.
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Trigger that caused the promotion.
    /// </summary>
    public PromotionTriggerType Trigger { get; init; }

    /// <summary>
    /// IDs of the promoted items.
    /// </summary>
    public IReadOnlyList<Guid> PromotedIds { get; init; } = [];

    /// <summary>
    /// Error message if promotion failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful promotion result.
    /// </summary>
    public static PromotionResult Succeeded(
        int count,
        int tokens,
        PromotionTriggerType trigger,
        IReadOnlyList<Guid> ids) => new()
    {
        Success = true,
        PromotedCount = count,
        TotalTokens = tokens,
        Trigger = trigger,
        PromotedIds = ids
    };

    /// <summary>
    /// Creates a failed promotion result.
    /// </summary>
    public static PromotionResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };

    /// <summary>
    /// Creates an empty (no-op) result.
    /// </summary>
    public static PromotionResult Empty => new()
    {
        Success = true,
        PromotedCount = 0,
        TotalTokens = 0
    };
}

/// <summary>
/// Type of trigger that caused promotion.
/// </summary>
public enum PromotionTriggerType
{
    /// <summary>
    /// No trigger (manual or default).
    /// </summary>
    None = 0,

    /// <summary>
    /// Triggered by idle timeout.
    /// </summary>
    IdleTimeout = 1,

    /// <summary>
    /// Triggered by token threshold.
    /// </summary>
    TokenThreshold = 2,

    /// <summary>
    /// Triggered by turn count.
    /// </summary>
    TurnThreshold = 3,

    /// <summary>
    /// Triggered by topic change.
    /// </summary>
    TopicChange = 4,

    /// <summary>
    /// Triggered by session end.
    /// </summary>
    SessionEnd = 5,

    /// <summary>
    /// Triggered by confidence threshold (for Archive tier).
    /// </summary>
    ConfidenceThreshold = 6,

    /// <summary>
    /// Triggered by confirmation count threshold (for Archive tier).
    /// </summary>
    ConfirmationThreshold = 7,

    /// <summary>
    /// Triggered by explicit manual request.
    /// </summary>
    Manual = 8
}
