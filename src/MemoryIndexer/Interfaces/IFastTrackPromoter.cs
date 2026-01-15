using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Interface for fast-track promotion of high-confidence facts directly to Archive (T3).
/// Bypasses the standard Buffer → Short → Long → Archive pipeline for verified facts.
/// </summary>
/// <remarks>
/// Fast-Track Promotion Rules:
/// - Confidence ≥ 0.9: Direct to Archive with ConfirmationCount = 1
/// - Identity facts (name, age): Highest priority for fast-track
/// - Context must be Direct (not Quoted, Hypothetical, Narrative, etc.)
///
/// Standard vs Fast-Track:
/// - Standard: Buffer → Short → Long → Archive (requires 3 confirmations)
/// - Fast-Track: Buffer → Archive (single high-confidence statement)
///
/// Integration Points:
/// - Called during buffer drain in SensoryPromoterService
/// - Uses IFactExtractor for fact detection
/// - Uses IArchiveStore for semantic storage
/// </remarks>
public interface IFastTrackPromoter
{
    /// <summary>
    /// Processes content for fast-track fact promotion.
    /// Extracts facts and promotes high-confidence ones directly to Archive.
    /// </summary>
    /// <param name="context">The fact extraction context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing promoted facts and decisions.</returns>
    Task<FastTrackResult> ProcessAsync(
        FactExtractionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes multiple conversation items for fact extraction and promotion.
    /// Used during buffer drain to extract facts from conversations.
    /// </summary>
    /// <param name="items">The conversation items to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing all promoted facts across items.</returns>
    Task<FastTrackBatchResult> ProcessBatchAsync(
        IReadOnlyList<SensoryMemory> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user profile facts from the Archive store.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User profile with extracted facts.</returns>
    Task<UserProfile> GetUserProfileAsync(
        string userId,
        FactCategory? category = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single fast-track promotion operation.
/// </summary>
public sealed class FastTrackResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// All facts extracted from the content.
    /// </summary>
    public IReadOnlyList<UserFact> ExtractedFacts { get; init; } = [];

    /// <summary>
    /// Facts that were promoted to Archive via fast-track.
    /// </summary>
    public IReadOnlyList<UserFact> FastTrackedFacts { get; init; } = [];

    /// <summary>
    /// Facts queued for standard promotion path.
    /// </summary>
    public IReadOnlyList<UserFact> StandardPathFacts { get; init; } = [];

    /// <summary>
    /// Facts that were skipped (duplicates, conflicts, etc.).
    /// </summary>
    public IReadOnlyList<FactSkipReason> SkippedFacts { get; init; } = [];

    /// <summary>
    /// The detected context type.
    /// </summary>
    public StatementContextType ContextType { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates an empty successful result.
    /// </summary>
    public static FastTrackResult Empty => new()
    {
        Success = true,
        ContextType = StatementContextType.Unknown
    };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static FastTrackResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Result of batch fast-track promotion.
/// </summary>
public sealed class FastTrackBatchResult
{
    /// <summary>
    /// Whether the batch operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of items processed.
    /// </summary>
    public int ItemsProcessed { get; init; }

    /// <summary>
    /// Total facts extracted across all items.
    /// </summary>
    public int TotalFactsExtracted { get; init; }

    /// <summary>
    /// Total facts fast-tracked to Archive.
    /// </summary>
    public int TotalFastTracked { get; init; }

    /// <summary>
    /// Total facts queued for standard path.
    /// </summary>
    public int TotalStandardPath { get; init; }

    /// <summary>
    /// Total facts skipped.
    /// </summary>
    public int TotalSkipped { get; init; }

    /// <summary>
    /// Individual results per item.
    /// </summary>
    public IReadOnlyList<FastTrackResult> Results { get; init; } = [];

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Duration of the batch operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Creates an empty successful result.
    /// </summary>
    public static FastTrackBatchResult Empty => new()
    {
        Success = true,
        ItemsProcessed = 0
    };
}

/// <summary>
/// Reason for skipping a fact during promotion.
/// </summary>
public sealed class FactSkipReason
{
    /// <summary>
    /// The fact that was skipped.
    /// </summary>
    public required UserFact Fact { get; init; }

    /// <summary>
    /// The reason for skipping.
    /// </summary>
    public required SkipReasonType Reason { get; init; }

    /// <summary>
    /// Additional details about the skip.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Types of skip reasons.
/// </summary>
public enum SkipReasonType
{
    /// <summary>
    /// Exact duplicate exists.
    /// </summary>
    Duplicate,

    /// <summary>
    /// Semantic duplicate exists (same meaning).
    /// </summary>
    SemanticDuplicate,

    /// <summary>
    /// Conflict with existing fact.
    /// </summary>
    Conflict,

    /// <summary>
    /// Confidence too low for any promotion.
    /// </summary>
    LowConfidence,

    /// <summary>
    /// Context type prevents promotion (quoted, hypothetical, etc.).
    /// </summary>
    ContextNotDirect,

    /// <summary>
    /// Requires manual review due to conflict.
    /// </summary>
    RequiresReview,

    /// <summary>
    /// Other reason.
    /// </summary>
    Other
}

/// <summary>
/// User profile containing extracted facts.
/// </summary>
public sealed class UserProfile
{
    /// <summary>
    /// The user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// All facts in the profile.
    /// </summary>
    public IReadOnlyList<UserProfileFact> Facts { get; init; } = [];

    /// <summary>
    /// Facts grouped by category.
    /// </summary>
    public IReadOnlyDictionary<FactCategory, IReadOnlyList<UserProfileFact>> FactsByCategory { get; init; }
        = new Dictionary<FactCategory, IReadOnlyList<UserProfileFact>>();

    /// <summary>
    /// Total number of facts.
    /// </summary>
    public int TotalFacts => Facts.Count;

    /// <summary>
    /// Number of confirmed facts.
    /// </summary>
    public int ConfirmedFacts => Facts.Count(f => f.IsConfirmed);

    /// <summary>
    /// When the profile was last updated.
    /// </summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>
    /// Creates an empty profile.
    /// </summary>
    public static UserProfile Empty(string userId) => new()
    {
        UserId = userId,
        Facts = [],
        FactsByCategory = new Dictionary<FactCategory, IReadOnlyList<UserProfileFact>>()
    };
}

/// <summary>
/// A single fact in the user profile.
/// </summary>
public sealed class UserProfileFact
{
    /// <summary>
    /// The unique key for this fact.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The fact content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The category of this fact.
    /// </summary>
    public FactCategory Category { get; init; }

    /// <summary>
    /// Confidence score (0-1).
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Number of confirmations.
    /// </summary>
    public int ConfirmationCount { get; init; }

    /// <summary>
    /// Whether this fact is confirmed (meets AND logic).
    /// </summary>
    public bool IsConfirmed { get; init; }

    /// <summary>
    /// When this fact was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When this fact was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// The subject of the fact (e.g., "user", "user's mother").
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The predicate of the fact (e.g., "name", "age").
    /// </summary>
    public string? Predicate { get; init; }

    /// <summary>
    /// The object/value of the fact (e.g., "John", "30").
    /// </summary>
    public string? Object { get; init; }
}
