using MemoryIndexer.Models;

namespace MemoryIndexer.Sdk.Intelligence.Operations;

/// <summary>
/// Service that decides what operation to perform when new content arrives.
/// Implements intelligent memory management by analyzing content semantics.
/// </summary>
/// <remarks>
/// Based on research patterns from:
/// - MemGPT (self-editing memory operations)
/// - Stanford Generative Agents (memory reflection)
/// - AI memory consolidation patterns
///
/// The decision process considers:
/// - Content novelty and importance
/// - Similarity to existing memories
/// - Contradiction detection
/// - Temporal relevance
/// </remarks>
public interface IMemoryOperationDecider
{
    /// <summary>
    /// Decides what operation to perform for incoming content.
    /// </summary>
    /// <param name="content">The incoming content to evaluate.</param>
    /// <param name="userId">User/tenant ID for scoped evaluation.</param>
    /// <param name="options">Decision options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recommended operation with details.</returns>
    Task<OperationDecision> DecideAsync(
        string content,
        string userId,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a batch of content items for operation decisions.
    /// </summary>
    /// <param name="contents">Content items to evaluate.</param>
    /// <param name="userId">User/tenant ID.</param>
    /// <param name="options">Decision options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decisions for each content item.</returns>
    Task<IReadOnlyList<OperationDecision>> DecideBatchAsync(
        IReadOnlyList<string> contents,
        string userId,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The type of operation to perform on memory.
/// </summary>
public enum MemoryOperation
{
    /// <summary>
    /// Create a new memory entry.
    /// Content is novel and valuable.
    /// </summary>
    Add = 0,

    /// <summary>
    /// Update an existing memory.
    /// Content enriches or refines existing knowledge.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Delete an existing memory.
    /// Content indicates existing memory is outdated or incorrect.
    /// </summary>
    Delete = 2,

    /// <summary>
    /// Do nothing.
    /// Content is duplicate, low-value, or inappropriate.
    /// </summary>
    Noop = 3,

    /// <summary>
    /// Merge with existing memory.
    /// Content should be combined with similar existing memory.
    /// </summary>
    Merge = 4,

    /// <summary>
    /// Replace an existing memory.
    /// New content supersedes old (e.g., preference update).
    /// </summary>
    Replace = 5
}

/// <summary>
/// Result of the operation decision process.
/// </summary>
public sealed class OperationDecision
{
    /// <summary>
    /// The recommended operation.
    /// </summary>
    public required MemoryOperation Operation { get; init; }

    /// <summary>
    /// Confidence in the decision (0.0 to 1.0).
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// Reasoning for the decision.
    /// </summary>
    public required string Reasoning { get; init; }

    /// <summary>
    /// The content that was evaluated.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Target memory for Update/Delete/Merge/Replace operations.
    /// Null for Add/Noop operations.
    /// </summary>
    public MemoryUnit? TargetMemory { get; init; }

    /// <summary>
    /// Additional memories related to this decision (for Merge).
    /// </summary>
    public IReadOnlyList<MemoryUnit> RelatedMemories { get; init; } = [];

    /// <summary>
    /// Suggested merged/updated content (for Merge/Update operations).
    /// </summary>
    public string? SuggestedContent { get; init; }

    /// <summary>
    /// Importance score of the content.
    /// </summary>
    public float ImportanceScore { get; init; }

    /// <summary>
    /// Detected memory type for the content.
    /// </summary>
    public MemoryType SuggestedType { get; init; }

    /// <summary>
    /// Extracted topics from the content.
    /// </summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>
    /// Whether a contradiction was detected with existing memory.
    /// </summary>
    public bool ContradictionDetected { get; init; }

    /// <summary>
    /// Details about the contradiction if detected.
    /// </summary>
    public string? ContradictionDetails { get; init; }
}

/// <summary>
/// Options for the decision process.
/// </summary>
public sealed class DecisionOptions
{
    /// <summary>
    /// Similarity threshold for considering memories as duplicates (default: 0.85).
    /// </summary>
    public float DuplicateThreshold { get; init; } = 0.85f;

    /// <summary>
    /// Similarity threshold for considering memories as related (default: 0.70).
    /// </summary>
    public float RelatedThreshold { get; init; } = 0.70f;

    /// <summary>
    /// Minimum importance score to consider content valuable (default: 0.3).
    /// </summary>
    public float MinimumImportance { get; init; } = 0.3f;

    /// <summary>
    /// Maximum memories to compare against (default: 20).
    /// </summary>
    public int MaxComparisons { get; init; } = 20;

    /// <summary>
    /// Whether to detect contradictions (default: true).
    /// </summary>
    public bool DetectContradictions { get; init; } = true;

    /// <summary>
    /// Session ID to scope the decision (null = all sessions).
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Preferred memory type for new memories (null = auto-detect).
    /// </summary>
    public MemoryType? PreferredType { get; init; }
}
