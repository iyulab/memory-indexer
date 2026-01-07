using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Detects and resolves semantic conflicts between memories.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
/// <remarks>
/// Provides general-purpose conflict detection and resolution without
/// domain-specific logic. Handles:
/// - Deduplication (identical semantic meaning)
/// - Refinement (new info adds detail)
/// - Updates (same fact, changed value)
/// - Contradictions (direct conflicts)
/// - Temporal evolution (preferences change over time)
///
/// Example: "User likes apples" → "User doesn't eat apples after getting sick"
/// Resolution: Archive old memory with temporal context, add new as current.
/// </remarks>
public interface IMemoryConflictResolver
{
    /// <summary>
    /// Analyzes relationship between new and existing memories.
    /// Determines appropriate action: ADD, NO-OP, REPLACE, MERGE, ARCHIVE, MARK_CONFLICT.
    /// </summary>
    /// <param name="newMemory">New memory candidate for storage.</param>
    /// <param name="similarMemories">Existing memories with semantic similarity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution containing recommended action and reasoning.</returns>
    Task<ConflictResolution> ResolveAsync(
        MemoryUnit newMemory,
        IReadOnlyList<MemoryUnit> similarMemories,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of conflict resolution analysis.
/// </summary>
public sealed class ConflictResolution
{
    /// <summary>
    /// Recommended action for the new memory.
    /// </summary>
    public required MemoryAction Action { get; init; }

    /// <summary>
    /// Type of conflict detected.
    /// </summary>
    public required ConflictType ConflictType { get; init; }

    /// <summary>
    /// Confidence in the resolution decision (0-1).
    /// Higher values indicate more certain analysis.
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// If Action is UPDATE, MERGE, or ARCHIVE, this is the target memory ID.
    /// </summary>
    public string? TargetMemoryId { get; init; }

    /// <summary>
    /// If Action is MERGE or ARCHIVE, this contains updated content.
    /// </summary>
    public string? UpdatedContent { get; init; }

    /// <summary>
    /// Human-readable reasoning for the decision.
    /// Used for debugging and transparency.
    /// </summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// Actions to take when storing a new memory.
/// </summary>
public enum MemoryAction
{
    /// <summary>
    /// Add as new memory (no conflict detected).
    /// </summary>
    Add,

    /// <summary>
    /// Skip storing (duplicate exists with identical semantic meaning).
    /// Example: "likes pizza" vs "enjoys pizza"
    /// </summary>
    NoOp,

    /// <summary>
    /// Replace existing memory with new one.
    /// Used for updates (e.g., "age 25" → "age 26") or when new memory
    /// has significantly higher confidence/recency.
    /// </summary>
    Replace,

    /// <summary>
    /// Merge new information into existing memory.
    /// Example: "likes pizza" + "favorite is margherita" → "likes pizza, especially margherita"
    /// </summary>
    Merge,

    /// <summary>
    /// Archive old memory as historical context, add new as current.
    /// Used for temporal evolution: "liked apples" → "doesn't eat apples anymore"
    /// Old memory marked with ValidUntil timestamp.
    /// </summary>
    Archive,

    /// <summary>
    /// Store both memories with conflict marker for human review.
    /// Used when confidence is too low to resolve automatically.
    /// </summary>
    MarkConflict
}

/// <summary>
/// Types of semantic conflicts between memories.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// No conflict detected, different topics.
    /// </summary>
    None,

    /// <summary>
    /// Identical semantic meaning (paraphrase).
    /// Example: "likes pizza" ≈ "enjoys pizza"
    /// </summary>
    Duplicate,

    /// <summary>
    /// New memory adds detail to existing (refinement).
    /// Example: "likes pizza" → "loves margherita pizza"
    /// </summary>
    Refinement,

    /// <summary>
    /// Same fact, updated value.
    /// Example: "age 25" → "age 26"
    /// </summary>
    Update,

    /// <summary>
    /// Direct contradiction between facts.
    /// Example: "likes apples" vs "dislikes apples"
    /// </summary>
    Contradiction,

    /// <summary>
    /// Time-based evolution (preferences/facts change over time).
    /// Example: "used to smoke" vs "quit smoking 2 years ago"
    /// </summary>
    Temporal
}
