using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// User Profile (L3 Tier) interface for persistent user knowledge.
/// Stores long-term facts, preferences, and accumulated session knowledge.
/// </summary>
/// <remarks>
/// 4-Tier Architecture:
/// - Recently (Buffer): Raw conversation staging
/// - Working (L1): Topic-grouped active context
/// - Session (L2): Archived session summaries
/// - User (L3): Profile dictionary - THIS TIER
///
/// Promotion from Session→User uses AND logic:
/// - Minimum confirmation count (3 sessions)
/// - High confidence threshold (0.8)
/// - Consistency across sessions
/// </remarks>
public interface IUserProfile
{
    /// <summary>
    /// Gets a user profile entry by key.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The profile key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile entry if found.</returns>
    Task<UserProfileEntry?> GetAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all profile entries for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All profile entries.</returns>
    Task<IReadOnlyList<UserProfileEntry>> GetAllAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets profile entries by category.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="category">The category to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Profile entries in the specified category.</returns>
    Task<IReadOnlyList<UserProfileEntry>> GetByCategoryAsync(
        string userId,
        UserProfileCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates a profile entry.
    /// Creates new entry if key doesn't exist.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="entry">The profile entry to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if created new.</returns>
    Task<bool> SetAsync(
        string userId,
        UserProfileEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an existing profile entry.
    /// Increments confirmation count and updates confidence.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The profile key to confirm.</param>
    /// <param name="evidence">Optional evidence for the confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated entry if found.</returns>
    Task<UserProfileEntry?> ConfirmAsync(
        string userId,
        string key,
        string? evidence = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a profile entry.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="key">The profile key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removed.</returns>
    Task<bool> RemoveAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches profile entries by content similarity.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching profile entries.</returns>
    Task<IReadOnlyList<UserProfileEntry>> SearchAsync(
        string userId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets profile statistics for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Profile statistics.</returns>
    UserProfileStats GetStats(string userId);

    /// <summary>
    /// Checks if a user has any profile data.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if profile exists.</returns>
    bool HasProfile(string userId);
}

/// <summary>
/// A single entry in the user profile dictionary.
/// </summary>
public sealed class UserProfileEntry
{
    /// <summary>
    /// Unique key for this profile entry.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Human-readable value/fact.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Category of this profile entry.
    /// </summary>
    public UserProfileCategory Category { get; set; } = UserProfileCategory.Fact;

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
/// Categories for user profile entries.
/// </summary>
public enum UserProfileCategory
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
/// Statistics about a user's profile.
/// </summary>
public sealed class UserProfileStats
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Total number of profile entries.
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Number of confirmed entries.
    /// </summary>
    public int ConfirmedEntries { get; init; }

    /// <summary>
    /// Entries by category.
    /// </summary>
    public IReadOnlyDictionary<UserProfileCategory, int> EntriesByCategory { get; init; }
        = new Dictionary<UserProfileCategory, int>();

    /// <summary>
    /// Average confidence across all entries.
    /// </summary>
    public float AverageConfidence { get; init; }

    /// <summary>
    /// When the profile was first created.
    /// </summary>
    public DateTime? FirstEntryAt { get; init; }

    /// <summary>
    /// When the profile was last updated.
    /// </summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>
    /// Empty stats.
    /// </summary>
    public static UserProfileStats Empty(string userId) => new()
    {
        UserId = userId,
        TotalEntries = 0,
        ConfirmedEntries = 0,
        AverageConfidence = 0
    };
}

/// <summary>
/// Options for the user profile service.
/// </summary>
public sealed class UserProfileOptions
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
