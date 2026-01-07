using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Memory Primitives - The 12 fundamental operations for memory management.
/// These form the "instruction set" of the memory system.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 2.2 "Memory Primitives"
///
/// Organized into categories:
/// - Content Operations: Encode, Update, Split, Merge
/// - Lifecycle Operations: Delete, Expire, Lock
/// - Classification Operations: Label
/// - Retrieval Operations: Retrieve, Summarize
/// - Tier Operations: Promote, Demote
/// </remarks>
public interface IMemoryPrimitives
{
    #region Content Operations

    /// <summary>
    /// Encodes a new memory with embedding generation.
    /// Implements Tulving's Encoding Specificity Principle.
    /// </summary>
    /// <param name="request">Encoding request with content and metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encoded memory.</returns>
    Task<MemoryUnit> EncodeAsync(EncodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing memory's content.
    /// Implements memory reconsolidation theory.
    /// </summary>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated memory, or null if not found.</returns>
    Task<MemoryUnit?> UpdateAsync(UpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits a memory into multiple semantic units.
    /// Based on chunking theory for optimal retrieval.
    /// </summary>
    /// <param name="request">Split request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resulting memory chunks.</returns>
    Task<IReadOnlyList<MemoryUnit>> SplitAsync(SplitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges multiple related memories into one.
    /// Implements memory consolidation during "sleep" cycles.
    /// </summary>
    /// <param name="request">Merge request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged memory.</returns>
    Task<MemoryUnit> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default);

    #endregion

    #region Lifecycle Operations

    /// <summary>
    /// Deletes a memory (soft delete by default).
    /// Implements intentional forgetting.
    /// </summary>
    /// <param name="request">Delete request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found or locked.</returns>
    Task<bool> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates expiration for a memory.
    /// TTL-based automatic cleanup.
    /// </summary>
    /// <param name="request">Expire request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated memory, or null if not found.</returns>
    Task<MemoryUnit?> ExpireAsync(ExpireRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks or unlocks a memory to prevent automatic eviction/modification.
    /// Used for system prompts and core facts.
    /// </summary>
    /// <param name="request">Lock request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated memory, or null if not found.</returns>
    Task<MemoryUnit?> LockAsync(LockRequest request, CancellationToken cancellationToken = default);

    #endregion

    #region Classification Operations

    /// <summary>
    /// Labels a memory with type classification.
    /// Based on Tulving's memory taxonomy.
    /// </summary>
    /// <param name="request">Label request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The labeled memory, or null if not found.</returns>
    Task<MemoryUnit?> LabelAsync(LabelRequest request, CancellationToken cancellationToken = default);

    #endregion

    #region Retrieval Operations

    /// <summary>
    /// Retrieves memories using hybrid search.
    /// Combines semantic, keyword, recency, and importance scoring.
    /// </summary>
    /// <param name="request">Retrieve request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching memories with scores.</returns>
    Task<IReadOnlyList<RetrieveResult>> RetrieveAsync(
        RetrieveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a summary of one or more memories.
    /// Compresses while preserving essential information.
    /// </summary>
    /// <param name="request">Summarize request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary memory.</returns>
    Task<MemoryUnit> SummarizeAsync(SummarizeRequest request, CancellationToken cancellationToken = default);

    #endregion

    #region Tier Operations

    /// <summary>
    /// Promotes a memory to a higher tier.
    /// Page-in operation (L3→L2→L1).
    /// </summary>
    /// <param name="request">Promote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The promoted memory, or null if not found or at highest tier.</returns>
    Task<MemoryUnit?> PromoteAsync(PromoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Demotes a memory to a lower tier.
    /// Page-out operation (L1→L2→L3).
    /// </summary>
    /// <param name="request">Demote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The demoted memory, or null if not found or at lowest tier.</returns>
    Task<MemoryUnit?> DemoteAsync(DemoteRequest request, CancellationToken cancellationToken = default);

    #endregion
}

#region Request Types

/// <summary>
/// Request for encoding a new memory.
/// </summary>
public sealed class EncodeRequest
{
    /// <summary>
    /// User ID for the memory.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Optional session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Memory content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Memory type (auto-detected if not specified).
    /// </summary>
    public MemoryType? Type { get; init; }

    /// <summary>
    /// Initial tier (default: Session).
    /// </summary>
    public Tier Tier { get; init; } = Tier.Long;

    /// <summary>
    /// Importance score (0-1, auto-evaluated if not specified).
    /// </summary>
    public float? ImportanceScore { get; init; }

    /// <summary>
    /// Topics to associate with this memory.
    /// </summary>
    public List<string>? Topics { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// If true, memory is locked against automatic eviction.
    /// </summary>
    public bool IsLocked { get; init; }

    /// <summary>
    /// Optional expiration time.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// Request for updating a memory.
/// </summary>
public sealed class UpdateRequest
{
    /// <summary>
    /// Memory ID to update.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// New content (null to keep existing).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// If true, regenerate embedding for new content.
    /// </summary>
    public bool RegenerateEmbedding { get; init; } = true;

    /// <summary>
    /// ID of memory this update supersedes (for temporal tracking).
    /// </summary>
    public Guid? SupersedesId { get; init; }

    /// <summary>
    /// Confidence score for the update (0-1).
    /// </summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>
    /// Updated topics (null to keep existing).
    /// </summary>
    public List<string>? Topics { get; init; }

    /// <summary>
    /// Metadata to merge (null to keep existing).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request for splitting a memory.
/// </summary>
public sealed class SplitRequest
{
    /// <summary>
    /// Memory ID to split.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Split strategy.
    /// </summary>
    public SplitStrategy Strategy { get; init; } = SplitStrategy.Semantic;

    /// <summary>
    /// Maximum size per chunk (tokens or characters based on strategy).
    /// </summary>
    public int? MaxChunkSize { get; init; }

    /// <summary>
    /// Overlap between chunks (for sliding window strategy).
    /// </summary>
    public int? Overlap { get; init; }

    /// <summary>
    /// Whether to delete the original memory after splitting.
    /// </summary>
    public bool DeleteOriginal { get; init; } = true;
}

/// <summary>
/// Strategy for splitting memories.
/// </summary>
public enum SplitStrategy
{
    /// <summary>
    /// Split by semantic boundaries (sentences, paragraphs).
    /// </summary>
    Semantic,

    /// <summary>
    /// Split by fixed character count.
    /// </summary>
    FixedSize,

    /// <summary>
    /// Split by token count.
    /// </summary>
    TokenBased,

    /// <summary>
    /// Sliding window with overlap.
    /// </summary>
    SlidingWindow
}

/// <summary>
/// Request for merging memories.
/// </summary>
public sealed class MergeRequest
{
    /// <summary>
    /// Memory IDs to merge.
    /// </summary>
    public required IReadOnlyList<Guid> MemoryIds { get; init; }

    /// <summary>
    /// Merge strategy.
    /// </summary>
    public MemoryMergeStrategy Strategy { get; init; } = MemoryMergeStrategy.Concatenate;

    /// <summary>
    /// Whether to delete source memories after merging.
    /// </summary>
    public bool DeleteSources { get; init; } = true;

    /// <summary>
    /// Type for the merged memory (auto-detected if not specified).
    /// </summary>
    public MemoryType? ResultType { get; init; }
}

/// <summary>
/// Strategy for merging memories (content processing).
/// </summary>
public enum MemoryMergeStrategy
{
    /// <summary>
    /// Simple concatenation with separators.
    /// </summary>
    Concatenate,

    /// <summary>
    /// LLM-generated summary of merged content.
    /// </summary>
    Summarize,

    /// <summary>
    /// Extract and deduplicate key points.
    /// </summary>
    ExtractKeyPoints
}

/// <summary>
/// Request for deleting a memory.
/// </summary>
public sealed class DeleteRequest
{
    /// <summary>
    /// Memory ID to delete.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// If true, permanently delete; otherwise soft delete.
    /// </summary>
    public bool HardDelete { get; init; }

    /// <summary>
    /// If true, delete even if locked.
    /// </summary>
    public bool ForceLocked { get; init; }
}

/// <summary>
/// Request for setting expiration.
/// </summary>
public sealed class ExpireRequest
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Expiration time (null to remove expiration).
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// TTL duration (alternative to ExpiresAt).
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }
}

/// <summary>
/// Request for locking/unlocking a memory.
/// </summary>
public sealed class LockRequest
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// True to lock, false to unlock.
    /// </summary>
    public required bool IsLocked { get; init; }

    /// <summary>
    /// Reason for locking (for audit).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Request for labeling a memory.
/// </summary>
public sealed class LabelRequest
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Memory type to assign.
    /// </summary>
    public MemoryType? Type { get; init; }

    /// <summary>
    /// Topics to assign (replaces existing).
    /// </summary>
    public List<string>? Topics { get; init; }

    /// <summary>
    /// Topics to add (preserves existing).
    /// </summary>
    public List<string>? AddTopics { get; init; }

    /// <summary>
    /// Topics to remove.
    /// </summary>
    public List<string>? RemoveTopics { get; init; }

    /// <summary>
    /// Entities to assign.
    /// </summary>
    public List<string>? Entities { get; init; }
}

/// <summary>
/// Request for retrieving memories.
/// </summary>
public sealed class RetrieveRequest
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Query string for semantic search.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Optional session ID filter.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int Limit { get; init; } = 5;

    /// <summary>
    /// Minimum combined score threshold.
    /// </summary>
    public float MinScore { get; init; } = 0.3f;

    /// <summary>
    /// Tiers to search (null = all).
    /// </summary>
    public Tier[]? Tiers { get; init; }

    /// <summary>
    /// Memory types to include (null = all).
    /// </summary>
    public MemoryType[]? Types { get; init; }

    /// <summary>
    /// Whether to use Dynamic Alpha Tuning for weights.
    /// </summary>
    public bool EnableDAT { get; init; } = true;

    /// <summary>
    /// Manual weights (overrides DAT if specified).
    /// </summary>
    public RetrievalWeights? Weights { get; init; }

    /// <summary>
    /// Whether to automatically record access (updates retention).
    /// </summary>
    public bool RecordAccess { get; init; } = true;
}

/// <summary>
/// Weights for hybrid retrieval scoring.
/// </summary>
public sealed class RetrievalWeights
{
    /// <summary>
    /// Weight for semantic similarity (0-1).
    /// </summary>
    public float Semantic { get; init; } = 0.4f;

    /// <summary>
    /// Weight for keyword match (0-1).
    /// </summary>
    public float Keyword { get; init; } = 0.2f;

    /// <summary>
    /// Weight for recency (0-1).
    /// </summary>
    public float Recency { get; init; } = 0.2f;

    /// <summary>
    /// Weight for importance score (0-1).
    /// </summary>
    public float Importance { get; init; } = 0.2f;
}

/// <summary>
/// Result of a retrieval operation.
/// </summary>
public sealed class RetrieveResult
{
    /// <summary>
    /// The matched memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Combined relevance score (0-1).
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// Individual score components.
    /// </summary>
    public required ScoreBreakdown Breakdown { get; init; }
}

/// <summary>
/// Breakdown of retrieval score components.
/// </summary>
public sealed class ScoreBreakdown
{
    /// <summary>
    /// Final semantic similarity score (rerank score if available, else vector score).
    /// </summary>
    public float SemanticScore { get; init; }

    /// <summary>
    /// Keyword match score.
    /// </summary>
    public float KeywordScore { get; init; }

    /// <summary>
    /// Recency score.
    /// </summary>
    public float RecencyScore { get; init; }

    /// <summary>
    /// Importance score.
    /// </summary>
    public float ImportanceScore { get; init; }

    /// <summary>
    /// Retention score (from forgetting curve).
    /// </summary>
    public float RetentionScore { get; init; }

    /// <summary>
    /// Raw vector similarity score from embedding search.
    /// </summary>
    public float VectorScore { get; init; }

    /// <summary>
    /// Cross-encoder re-ranking score (null if reranking not applied).
    /// </summary>
    public float? RerankScore { get; init; }
}

/// <summary>
/// Request for summarizing memories.
/// </summary>
public sealed class SummarizeRequest
{
    /// <summary>
    /// Memory IDs to summarize.
    /// </summary>
    public required IReadOnlyList<Guid> MemoryIds { get; init; }

    /// <summary>
    /// Maximum length of summary (tokens).
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Whether to preserve source memories.
    /// </summary>
    public bool PreserveSources { get; init; } = true;

    /// <summary>
    /// Optional focus topic for summary.
    /// </summary>
    public string? FocusTopic { get; init; }
}

/// <summary>
/// Request for promoting a memory.
/// </summary>
public sealed class PromoteRequest
{
    /// <summary>
    /// Memory ID to promote.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Target tier (null = next higher tier).
    /// </summary>
    public Tier? TargetTier { get; init; }
}

/// <summary>
/// Request for demoting a memory.
/// </summary>
public sealed class DemoteRequest
{
    /// <summary>
    /// Memory ID to demote.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Target tier (null = next lower tier).
    /// </summary>
    public Tier? TargetTier { get; init; }

    /// <summary>
    /// Reason for demotion.
    /// </summary>
    public DemoteReason Reason { get; init; } = DemoteReason.Manual;
}

/// <summary>
/// Reason for memory demotion.
/// </summary>
public enum DemoteReason
{
    /// <summary>
    /// Manual demotion by user.
    /// </summary>
    Manual,

    /// <summary>
    /// Evicted due to capacity constraints.
    /// </summary>
    CapacityEviction,

    /// <summary>
    /// Low retention score.
    /// </summary>
    LowRetention,

    /// <summary>
    /// Session ended.
    /// </summary>
    SessionEnd,

    /// <summary>
    /// Context optimization.
    /// </summary>
    ContextOptimization
}

#endregion
