namespace MemoryIndexer.Interfaces;

/// <summary>
/// Defines retention policy for archive store entries.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public interface IRetentionPolicy
{
    /// <summary>
    /// Gets the retention rule for a specific category.
    /// </summary>
    /// <param name="category">The semantic store category.</param>
    /// <returns>The retention rule for that category.</returns>
    RetentionRule GetRule(SemanticStoreCategory category);

    /// <summary>
    /// Gets all retention rules.
    /// </summary>
    /// <returns>Dictionary mapping categories to their rules.</returns>
    IReadOnlyDictionary<SemanticStoreCategory, RetentionRule> GetAllRules();

    /// <summary>
    /// Evaluates whether an entry should be retained.
    /// </summary>
    /// <param name="entry">The entry to evaluate.</param>
    /// <param name="referenceTime">Time to evaluate against (defaults to now).</param>
    /// <returns>Retention decision with reason.</returns>
    RetentionDecision Evaluate(SemanticStoreEntry entry, DateTime? referenceTime = null);

    /// <summary>
    /// Evaluates multiple entries for retention.
    /// </summary>
    /// <param name="entries">Entries to evaluate.</param>
    /// <param name="referenceTime">Time to evaluate against (defaults to now).</param>
    /// <returns>List of retention decisions.</returns>
    IReadOnlyList<RetentionDecision> EvaluateAll(
        IEnumerable<SemanticStoreEntry> entries,
        DateTime? referenceTime = null);
}

/// <summary>
/// Service for applying retention policies.
/// </summary>
public interface IRetentionPolicyService
{
    /// <summary>
    /// Gets the current retention policy.
    /// </summary>
    IRetentionPolicy Policy { get; }

    /// <summary>
    /// Previews what would be cleaned up without actually deleting.
    /// </summary>
    /// <param name="userId">User ID to preview cleanup for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preview of cleanup actions.</returns>
    Task<RetentionPreview> PreviewCleanupAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies retention policy to a user's archive.
    /// </summary>
    /// <param name="userId">User ID to apply policy to.</param>
    /// <param name="dryRun">If true, only reports what would be done.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the cleanup operation.</returns>
    Task<RetentionResult> ApplyAsync(
        string userId,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies retention policy to all users.
    /// </summary>
    /// <param name="dryRun">If true, only reports what would be done.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated results across all users.</returns>
    Task<RetentionResult> ApplyToAllAsync(
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Retention rule for a category.
/// </summary>
public class RetentionRule
{
    /// <summary>
    /// Category this rule applies to.
    /// </summary>
    public SemanticStoreCategory Category { get; init; }

    /// <summary>
    /// Maximum age in days before entry is eligible for cleanup.
    /// Null means never expire based on age.
    /// </summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>
    /// Minimum confidence to retain.
    /// Entries below this confidence (after decay) are eligible for cleanup.
    /// </summary>
    public float MinConfidence { get; init; } = 0.1f;

    /// <summary>
    /// Minimum confirmation count to be protected from cleanup.
    /// Entries with confirmations >= this value are never deleted.
    /// </summary>
    public int ProtectedConfirmationCount { get; init; } = 5;

    /// <summary>
    /// Days since last access before entry becomes stale.
    /// Null means access patterns don't affect retention.
    /// </summary>
    public int? StaleAfterDays { get; init; }

    /// <summary>
    /// Whether to archive (soft delete) or permanently delete.
    /// </summary>
    public RetentionAction Action { get; init; } = RetentionAction.Archive;

    /// <summary>
    /// Human-readable description of the rule.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Action to take when retention criteria are met.
/// </summary>
public enum RetentionAction
{
    /// <summary>
    /// Keep the entry (no action).
    /// </summary>
    Keep,

    /// <summary>
    /// Soft delete (mark as inactive).
    /// </summary>
    Archive,

    /// <summary>
    /// Permanently delete.
    /// </summary>
    Delete
}

/// <summary>
/// Reason for retention decision.
/// </summary>
public enum RetentionReason
{
    /// <summary>
    /// Entry passes all retention criteria.
    /// </summary>
    WithinPolicy,

    /// <summary>
    /// Entry is protected by confirmation count.
    /// </summary>
    ProtectedByConfirmations,

    /// <summary>
    /// Entry was recently accessed.
    /// </summary>
    RecentlyAccessed,

    /// <summary>
    /// Entry exceeded maximum age.
    /// </summary>
    AgeExceeded,

    /// <summary>
    /// Entry confidence dropped below threshold.
    /// </summary>
    LowConfidence,

    /// <summary>
    /// Entry hasn't been accessed in too long.
    /// </summary>
    Stale,

    /// <summary>
    /// Entry is already archived (inactive).
    /// </summary>
    AlreadyArchived,

    /// <summary>
    /// Category has no expiration (infinite retention).
    /// </summary>
    NeverExpires
}

/// <summary>
/// Decision about whether to retain an entry.
/// </summary>
public class RetentionDecision
{
    /// <summary>
    /// The entry being evaluated.
    /// </summary>
    public required SemanticStoreEntry Entry { get; init; }

    /// <summary>
    /// Whether to retain the entry.
    /// </summary>
    public bool ShouldRetain { get; init; }

    /// <summary>
    /// Recommended action if not retaining.
    /// </summary>
    public RetentionAction Action { get; init; }

    /// <summary>
    /// Primary reason for the decision.
    /// </summary>
    public RetentionReason Reason { get; init; }

    /// <summary>
    /// The rule that was applied.
    /// </summary>
    public RetentionRule? AppliedRule { get; init; }

    /// <summary>
    /// Days until entry becomes eligible for cleanup.
    /// Null if never or already eligible.
    /// </summary>
    public int? DaysUntilEligible { get; init; }

    /// <summary>
    /// Current decayed confidence (if calculated).
    /// </summary>
    public float? DecayedConfidence { get; init; }
}

/// <summary>
/// Preview of what retention policy would clean up.
/// </summary>
public class RetentionPreview
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Total entries evaluated.
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Entries that would be retained.
    /// </summary>
    public int RetainCount { get; init; }

    /// <summary>
    /// Entries that would be archived.
    /// </summary>
    public int ArchiveCount { get; init; }

    /// <summary>
    /// Entries that would be deleted.
    /// </summary>
    public int DeleteCount { get; init; }

    /// <summary>
    /// Breakdown by category.
    /// </summary>
    public IReadOnlyDictionary<SemanticStoreCategory, CategoryPreview> ByCategory { get; init; }
        = new Dictionary<SemanticStoreCategory, CategoryPreview>();

    /// <summary>
    /// Detailed decisions for entries that would be cleaned up.
    /// </summary>
    public IReadOnlyList<RetentionDecision> CleanupDecisions { get; init; } = [];
}

/// <summary>
/// Preview for a specific category.
/// </summary>
public class CategoryPreview
{
    /// <summary>
    /// Category.
    /// </summary>
    public SemanticStoreCategory Category { get; init; }

    /// <summary>
    /// Total entries in category.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Entries to retain.
    /// </summary>
    public int RetainCount { get; init; }

    /// <summary>
    /// Entries to clean up.
    /// </summary>
    public int CleanupCount { get; init; }
}

/// <summary>
/// Result of applying retention policy.
/// </summary>
public class RetentionResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total entries processed.
    /// </summary>
    public int TotalProcessed { get; init; }

    /// <summary>
    /// Entries retained.
    /// </summary>
    public int RetainedCount { get; init; }

    /// <summary>
    /// Entries archived.
    /// </summary>
    public int ArchivedCount { get; init; }

    /// <summary>
    /// Entries deleted.
    /// </summary>
    public int DeletedCount { get; init; }

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>
    /// When the policy was applied.
    /// </summary>
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Users processed (for ApplyToAll).
    /// </summary>
    public int UsersProcessed { get; init; }

    /// <summary>
    /// Errors encountered (for ApplyToAll).
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Options for configuring retention policy.
/// </summary>
public class RetentionPolicyOptions
{
    /// <summary>
    /// Default maximum age in days for categories without specific rules.
    /// </summary>
    public int DefaultMaxAgeDays { get; set; } = 365;

    /// <summary>
    /// Default minimum confidence threshold.
    /// </summary>
    public float DefaultMinConfidence { get; set; } = 0.1f;

    /// <summary>
    /// Default stale threshold in days.
    /// </summary>
    public int DefaultStaleAfterDays { get; set; } = 180;

    /// <summary>
    /// Category-specific overrides.
    /// </summary>
    public Dictionary<SemanticStoreCategory, RetentionRule> CategoryRules { get; set; } = new();

    /// <summary>
    /// Whether to use confidence decay when evaluating.
    /// </summary>
    public bool UseConfidenceDecay { get; set; } = true;
}
