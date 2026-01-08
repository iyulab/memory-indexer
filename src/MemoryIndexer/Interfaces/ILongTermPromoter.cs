using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Orchestrates Long-Term (Tier 2) to Archive (Tier 3) memory promotion.
/// Implements Tulving's Episodic→Semantic memory transition with AND logic.
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - Buffer (T0): Sensory input
/// - Short (T1): Working memory
/// - Long (T2): Episodic memory - SOURCE TIER
/// - Archive (T3): Semantic memory - TARGET TIER
///
/// AND logic promotion requirements:
/// - Confidence >= 0.8 (high certainty threshold)
/// - ConfirmCount >= 3 (multiple confirmations across sessions)
///
/// This represents the transition from context-bound episodic memories
/// to context-free semantic knowledge, as described by Tulving (1972).
/// </remarks>
public interface ILongTermPromoter
{
    /// <summary>
    /// Checks for Long tier memories eligible for Archive promotion.
    /// </summary>
    /// <param name="userId">The user ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of memories eligible for Archive promotion.</returns>
    Task<IReadOnlyList<ArchivePromotionCandidate>> CheckPromotionCandidatesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes eligible Long tier memories to Archive tier.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the promotion operation.</returns>
    Task<ArchivePromotionResult> PromoteToArchiveAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes a specific memory to Archive tier.
    /// </summary>
    /// <param name="memory">The memory to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the promotion.</returns>
    Task<ArchivePromotionResult> PromoteMemoryAsync(
        MemoryUnit memory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all users with Long tier memories that may be eligible for promotion.
    /// </summary>
    /// <returns>List of user IDs with potential candidates.</returns>
    Task<IReadOnlyList<string>> GetUsersWithCandidatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A memory candidate for Archive promotion with eligibility details.
/// </summary>
public sealed record ArchivePromotionCandidate
{
    /// <summary>
    /// The memory being considered for promotion.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Whether the memory meets Archive AND logic requirements.
    /// </summary>
    public bool IsEligible { get; init; }

    /// <summary>
    /// Current confidence score.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Current confirmation count.
    /// </summary>
    public int ConfirmCount { get; init; }

    /// <summary>
    /// Required confidence threshold.
    /// </summary>
    public float RequiredConfidence { get; init; }

    /// <summary>
    /// Required confirmation count.
    /// </summary>
    public int RequiredConfirmCount { get; init; }

    /// <summary>
    /// Explanation of eligibility status.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Result of an Archive promotion operation.
/// </summary>
public sealed record ArchivePromotionResult
{
    /// <summary>
    /// Whether the promotion succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of memories promoted to Archive.
    /// </summary>
    public int MemoriesPromoted { get; init; }

    /// <summary>
    /// Number of memories that didn't meet AND logic requirements.
    /// </summary>
    public int MemoriesSkipped { get; init; }

    /// <summary>
    /// Details of promoted memories.
    /// </summary>
    public IReadOnlyList<PromotedMemoryInfo> PromotedMemories { get; init; } = [];

    /// <summary>
    /// Error message if promotion failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Duration of the promotion operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Empty/no-op result.
    /// </summary>
    public static ArchivePromotionResult Empty => new()
    {
        Success = true,
        MemoriesPromoted = 0,
        MemoriesSkipped = 0
    };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static ArchivePromotionResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Information about a successfully promoted memory.
/// </summary>
public sealed record PromotedMemoryInfo
{
    /// <summary>
    /// The memory ID.
    /// </summary>
    public Guid MemoryId { get; init; }

    /// <summary>
    /// Content summary.
    /// </summary>
    public string ContentSummary { get; init; } = string.Empty;

    /// <summary>
    /// Final confidence score.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Final confirmation count.
    /// </summary>
    public int ConfirmCount { get; init; }

    /// <summary>
    /// Memory type after promotion.
    /// </summary>
    public MemoryType FinalType { get; init; }
}
