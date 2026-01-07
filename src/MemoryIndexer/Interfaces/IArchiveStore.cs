using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Tier 3: Semantic Store interface for persistent user knowledge.
/// Stores context-free semantic knowledge extracted from episodic memories.
/// Implements Tulving's Semantic Memory System.
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - Buffer (T0): Raw sensory input
/// - Short-Term Memory (T1): Active processing
/// - LongTermStore (T2): Episodic memories
/// - ArchiveStore (T3): Semantic knowledge - THIS TIER
///
/// Semantic knowledge is:
/// - Context-free (no when/where)
/// - Generalized facts/concepts
/// - Abstracted from episodic experiences
/// - Highly consolidated and stable
///
/// Promotion from Episodic→Semantic uses AND logic:
/// - Minimum confirmation count (3 sessions)
/// - High confidence threshold (0.8)
/// - Consistency across episodes
/// </remarks>
public interface IArchiveStore
{
    /// <summary>
    /// Gets a semantic knowledge entry by key.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The knowledge key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The semantic entry if found.</returns>
    Task<SemanticStoreEntry?> GetAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all semantic entries for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All semantic entries.</returns>
    Task<IReadOnlyList<SemanticStoreEntry>> GetAllAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets semantic entries by category.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="category">The category to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Semantic entries in the specified category.</returns>
    Task<IReadOnlyList<SemanticStoreEntry>> GetByCategoryAsync(
        string userId,
        SemanticStoreCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates a semantic entry.
    /// Creates new entry if key doesn't exist.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="entry">The semantic entry to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if created new.</returns>
    Task<bool> SetAsync(
        string userId,
        SemanticStoreEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an existing semantic entry.
    /// Increments confirmation count and updates confidence.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The knowledge key to confirm.</param>
    /// <param name="evidence">Optional evidence for the confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated entry if found.</returns>
    Task<SemanticStoreEntry?> ConfirmAsync(
        string userId,
        string key,
        string? evidence = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a semantic entry.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The knowledge key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removed.</returns>
    Task<bool> RemoveAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches semantic entries by content similarity.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching semantic entries.</returns>
    Task<IReadOnlyList<SemanticStoreEntry>> SearchAsync(
        string userId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets semantic knowledge statistics for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Semantic store statistics.</returns>
    SemanticStoreStats GetStats(string userId);

    /// <summary>
    /// Checks if a user has any semantic knowledge.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if semantic store exists.</returns>
    bool HasProfile(string userId);
}

/// <summary>
/// A single entry in the semantic knowledge store.
/// </summary>
public sealed class SemanticStoreEntry
{
    /// <summary>
    /// Unique key for this semantic entry.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Human-readable value/fact.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Category of this semantic entry.
    /// </summary>
    public SemanticStoreCategory Category { get; set; } = SemanticStoreCategory.Fact;

    /// <summary>
    /// Confidence score (0-1).
    /// </summary>
    public float Confidence { get; set; } = 0.5f;

    /// <summary>
    /// Number of times this fact has been confirmed.
    /// </summary>
    public int ConfirmationCount { get; set; } = 1;

    /// <summary>
    /// Session IDs that contributed to this entry.
    /// </summary>
    public List<string> SourceSessions { get; init; } = [];

    /// <summary>
    /// When this entry was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this entry was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this entry was last accessed/used.
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional embedding for semantic search.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Whether this entry is fully confirmed (meets AND logic requirements).
    /// </summary>
    public bool IsConfirmed => ConfirmationCount >= 3 && Confidence >= 0.8f;
}

/// <summary>
/// Categories for semantic knowledge entries.
/// </summary>
public enum SemanticStoreCategory
{
    /// <summary>
    /// General fact about the user.
    /// </summary>
    Fact = 0,

    /// <summary>
    /// User preference or setting.
    /// </summary>
    Preference = 1,

    /// <summary>
    /// User's skill or expertise area.
    /// </summary>
    Skill = 2,

    /// <summary>
    /// User's interest or hobby.
    /// </summary>
    Interest = 3,

    /// <summary>
    /// User's relationship or social connection.
    /// </summary>
    Relationship = 4,

    /// <summary>
    /// User's work or professional context.
    /// </summary>
    Work = 5,

    /// <summary>
    /// User's goal or objective.
    /// </summary>
    Goal = 6,

    /// <summary>
    /// User's behavioral pattern.
    /// </summary>
    Behavior = 7,

    /// <summary>
    /// User's communication style preference.
    /// </summary>
    Communication = 8,

    /// <summary>
    /// Other/custom category.
    /// </summary>
    Other = 99
}

/// <summary>
/// Statistics about a user's semantic knowledge.
/// </summary>
public sealed class SemanticStoreStats
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Total number of semantic entries.
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Number of confirmed entries.
    /// </summary>
    public int ConfirmedEntries { get; init; }

    /// <summary>
    /// Entries by category.
    /// </summary>
    public IReadOnlyDictionary<SemanticStoreCategory, int> EntriesByCategory { get; init; }
        = new Dictionary<SemanticStoreCategory, int>();

    /// <summary>
    /// Average confidence across all entries.
    /// </summary>
    public float AverageConfidence { get; init; }

    /// <summary>
    /// When the semantic store was first created.
    /// </summary>
    public DateTime? FirstEntryAt { get; init; }

    /// <summary>
    /// When the semantic store was last updated.
    /// </summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>
    /// Empty stats.
    /// </summary>
    public static SemanticStoreStats Empty(string userId) => new()
    {
        UserId = userId,
        TotalEntries = 0,
        ConfirmedEntries = 0,
        AverageConfidence = 0
    };
}

/// <summary>
/// Options for the semantic store service.
/// </summary>
public sealed class SemanticStoreOptions
{
    /// <summary>
    /// Minimum confirmations required for an entry to be considered confirmed.
    /// Default: 3
    /// </summary>
    public int MinConfirmationCount { get; set; } = 3;

    /// <summary>
    /// Minimum confidence required for an entry to be considered confirmed.
    /// Default: 0.8
    /// </summary>
    public float MinConfidenceThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Confidence boost per confirmation.
    /// Default: 0.1
    /// </summary>
    public float ConfidenceBoostPerConfirmation { get; set; } = 0.1f;

    /// <summary>
    /// Maximum entries per user.
    /// Default: 500
    /// </summary>
    public int MaxEntriesPerUser { get; set; } = 500;

    /// <summary>
    /// Whether to enable semantic search.
    /// Default: true
    /// </summary>
    public bool EnableSemanticSearch { get; set; } = true;
}
