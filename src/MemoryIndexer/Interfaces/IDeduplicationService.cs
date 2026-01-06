using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for detecting and handling duplicate memories.
/// </summary>
public interface IDeduplicationService
{
    /// <summary>
    /// Checks if content is a duplicate of existing memories.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="similarityThreshold">Custom similarity threshold (optional).</param>
    /// <param name="contentType">Content type for type-aware deduplication (optional).</param>
    /// <param name="lookbackWindow">Number of recent memories to check (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Duplicate check result with recommended action.</returns>
    Task<DuplicateCheckResult> CheckForDuplicateAsync(
        string content,
        string userId,
        float? similarityThreshold = null,
        string? contentType = null,
        int? lookbackWindow = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a duplicate check operation.
/// </summary>
public sealed class DuplicateCheckResult
{
    /// <summary>
    /// Whether a duplicate was found.
    /// </summary>
    public required bool IsDuplicate { get; init; }

    /// <summary>
    /// Type of duplicate found.
    /// </summary>
    public required DuplicateType DuplicateType { get; init; }

    /// <summary>
    /// The existing memory that is a duplicate (if found).
    /// </summary>
    public MemoryUnit? ExistingMemory { get; init; }

    /// <summary>
    /// Similarity score with the most similar memory.
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Recommended action to take.
    /// </summary>
    public required DuplicateAction RecommendedAction { get; init; }

    /// <summary>
    /// List of similar memories found (optional).
    /// </summary>
    public List<MemorySearchResult>? SimilarMemories { get; init; }
}

/// <summary>
/// Type of duplicate detection.
/// </summary>
public enum DuplicateType
{
    /// <summary>
    /// No duplicate found.
    /// </summary>
    None,

    /// <summary>
    /// Exact content match (hash-based).
    /// </summary>
    Exact,

    /// <summary>
    /// Semantic/meaning-based match.
    /// </summary>
    Semantic
}

/// <summary>
/// Recommended action for handling duplicates.
/// </summary>
public enum DuplicateAction
{
    /// <summary>
    /// Add as new memory.
    /// </summary>
    Add,

    /// <summary>
    /// Skip - don't store the new content.
    /// </summary>
    Skip,

    /// <summary>
    /// Update the existing memory with new content.
    /// </summary>
    Update,

    /// <summary>
    /// Merge new and existing into one memory.
    /// </summary>
    Merge,

    /// <summary>
    /// Add but create a relationship to the similar memory.
    /// </summary>
    AddWithRelation
}
