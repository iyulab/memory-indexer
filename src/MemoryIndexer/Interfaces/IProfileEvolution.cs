namespace MemoryIndexer.Interfaces;

/// <summary>
/// Strategy for decaying fact confidence over time.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public interface IConfidenceDecayStrategy
{
    /// <summary>
    /// Strategy name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Calculates the decayed confidence for a fact.
    /// </summary>
    /// <param name="entry">The fact entry to evaluate.</param>
    /// <param name="asOfDate">Calculate decay as of this date.</param>
    /// <returns>Decayed confidence value (0-1).</returns>
    float CalculateDecayedConfidence(SemanticStoreEntry entry, DateTime asOfDate);

    /// <summary>
    /// Checks if a fact needs re-confirmation due to decay.
    /// </summary>
    /// <param name="entry">The fact entry to check.</param>
    /// <param name="threshold">Confidence threshold below which re-confirmation is needed.</param>
    /// <returns>True if re-confirmation is recommended.</returns>
    bool NeedsReconfirmation(SemanticStoreEntry entry, float threshold = 0.5f);
}

/// <summary>
/// Configuration for confidence decay.
/// </summary>
public class ConfidenceDecayOptions
{
    /// <summary>
    /// Default decay strategy.
    /// Default: "TimeBased"
    /// </summary>
    public string DefaultStrategy { get; set; } = "TimeBased";

    /// <summary>
    /// Half-life for time-based decay (days).
    /// After this period, unconfirmed facts lose half their confidence.
    /// Default: 90 days
    /// </summary>
    public int HalfLifeDays { get; set; } = 90;

    /// <summary>
    /// Minimum confidence floor (decay stops here).
    /// Default: 0.1
    /// </summary>
    public float MinimumConfidence { get; set; } = 0.1f;

    /// <summary>
    /// Bonus multiplier for each confirmation.
    /// Default: 1.2 (20% boost per confirmation)
    /// </summary>
    public float ConfirmationBonus { get; set; } = 1.2f;

    /// <summary>
    /// Bonus multiplier for recent access.
    /// Default: 1.1 (10% boost for recent access)
    /// </summary>
    public float AccessBonus { get; set; } = 1.1f;

    /// <summary>
    /// Days considered "recent" for access bonus.
    /// Default: 7 days
    /// </summary>
    public int RecentAccessDays { get; set; } = 7;

    /// <summary>
    /// Category-specific decay multipliers.
    /// Higher = slower decay.
    /// </summary>
    public Dictionary<FactCategory, float> CategoryMultipliers { get; set; } = new()
    {
        [FactCategory.Identity] = 2.0f,      // Identity facts decay slowly
        [FactCategory.Preference] = 0.8f,    // Preferences can change
        [FactCategory.Location] = 0.7f,      // Location changes relatively often
        [FactCategory.Professional] = 0.9f,  // Professional changes occasionally
        [FactCategory.Health] = 1.5f,        // Health facts are somewhat stable
        [FactCategory.Relationship] = 1.0f,  // Default decay
        [FactCategory.Skill] = 1.3f,         // Skills persist
        [FactCategory.Goal] = 0.6f,          // Goals change frequently
        [FactCategory.Temporal] = 0.5f,      // Temporal facts expire quickly
        [FactCategory.General] = 1.0f        // Default decay
    };
}

/// <summary>
/// Service for managing user profile snapshots.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public interface IProfileSnapshotService
{
    /// <summary>
    /// Creates a snapshot of a user's current profile.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="label">Optional label for this snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created snapshot.</returns>
    Task<ProfileSnapshot> CreateSnapshotAsync(
        string userId,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific snapshot by ID.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="snapshotId">Snapshot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot, or null if not found.</returns>
    Task<ProfileSnapshot?> GetSnapshotAsync(
        string userId,
        Guid snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all snapshots for a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="limit">Maximum number of snapshots to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of snapshots, newest first.</returns>
    Task<IReadOnlyList<ProfileSnapshotSummary>> ListSnapshotsAsync(
        string userId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two snapshots and returns the differences.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="olderSnapshotId">The older snapshot ID.</param>
    /// <param name="newerSnapshotId">The newer snapshot ID (or null for current profile).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The differences between snapshots.</returns>
    Task<ProfileDiff> CompareSnapshotsAsync(
        string userId,
        Guid olderSnapshotId,
        Guid? newerSnapshotId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets profile diff since last session.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="sessionId">Current session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Changes since last session.</returns>
    Task<ProfileDiff> GetChangeSinceLastSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a snapshot.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="snapshotId">Snapshot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteSnapshotAsync(
        string userId,
        Guid snapshotId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A point-in-time snapshot of a user's profile.
/// </summary>
public class ProfileSnapshot
{
    /// <summary>
    /// Unique snapshot ID.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Optional label for this snapshot.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// When this snapshot was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// All facts at the time of snapshot.
    /// </summary>
    public IReadOnlyList<SemanticStoreEntry> Facts { get; set; } = [];

    /// <summary>
    /// Statistics at snapshot time.
    /// </summary>
    public ProfileStats Stats { get; set; } = new();

    /// <summary>
    /// Session ID if snapshot was triggered by session.
    /// </summary>
    public string? SessionId { get; set; }
}

/// <summary>
/// Summary of a snapshot (without full fact data).
/// </summary>
public class ProfileSnapshotSummary
{
    /// <summary>
    /// Snapshot ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Optional label.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// When created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Total fact count at snapshot time.
    /// </summary>
    public int FactCount { get; set; }

    /// <summary>
    /// Session ID if applicable.
    /// </summary>
    public string? SessionId { get; set; }
}

/// <summary>
/// Statistics about a user's profile.
/// </summary>
public class ProfileStats
{
    /// <summary>
    /// Total number of facts.
    /// </summary>
    public int TotalFacts { get; set; }

    /// <summary>
    /// Number of confirmed facts (high confidence).
    /// </summary>
    public int ConfirmedFacts { get; set; }

    /// <summary>
    /// Number of facts needing re-confirmation.
    /// </summary>
    public int StaleFactCount { get; set; }

    /// <summary>
    /// Average confidence across all facts.
    /// </summary>
    public float AverageConfidence { get; set; }

    /// <summary>
    /// Facts by category.
    /// </summary>
    public IReadOnlyDictionary<SemanticStoreCategory, int> ByCategory { get; set; } =
        new Dictionary<SemanticStoreCategory, int>();

    /// <summary>
    /// Profile completeness score (0-1).
    /// </summary>
    public float CompletenessScore { get; set; }
}

/// <summary>
/// Differences between two profile states.
/// </summary>
public class ProfileDiff
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Older snapshot ID (null if comparing to empty).
    /// </summary>
    public Guid? OlderSnapshotId { get; set; }

    /// <summary>
    /// Newer snapshot ID (null if comparing to current).
    /// </summary>
    public Guid? NewerSnapshotId { get; set; }

    /// <summary>
    /// Timestamp of older state.
    /// </summary>
    public DateTime OlderTimestamp { get; set; }

    /// <summary>
    /// Timestamp of newer state.
    /// </summary>
    public DateTime NewerTimestamp { get; set; }

    /// <summary>
    /// Facts that were added.
    /// </summary>
    public IReadOnlyList<SemanticStoreEntry> AddedFacts { get; set; } = [];

    /// <summary>
    /// Facts that were removed.
    /// </summary>
    public IReadOnlyList<SemanticStoreEntry> RemovedFacts { get; set; } = [];

    /// <summary>
    /// Facts that were modified (key matches, value changed).
    /// </summary>
    public IReadOnlyList<FactChange> ModifiedFacts { get; set; } = [];

    /// <summary>
    /// Summary of changes.
    /// </summary>
    public DiffSummary Summary { get; set; } = new();

    /// <summary>
    /// Whether there are any changes.
    /// </summary>
    public bool HasChanges => AddedFacts.Count > 0 || RemovedFacts.Count > 0 || ModifiedFacts.Count > 0;
}

/// <summary>
/// A single fact that was modified.
/// </summary>
public class FactChange
{
    /// <summary>
    /// Fact key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Old value.
    /// </summary>
    public required string OldValue { get; set; }

    /// <summary>
    /// New value.
    /// </summary>
    public required string NewValue { get; set; }

    /// <summary>
    /// Old confidence.
    /// </summary>
    public float OldConfidence { get; set; }

    /// <summary>
    /// New confidence.
    /// </summary>
    public float NewConfidence { get; set; }

    /// <summary>
    /// Category of the fact.
    /// </summary>
    public SemanticStoreCategory Category { get; set; }

    /// <summary>
    /// Type of change.
    /// </summary>
    public FactChangeType ChangeType { get; set; }
}

/// <summary>
/// Type of fact change.
/// </summary>
public enum FactChangeType
{
    /// <summary>Value changed.</summary>
    ValueChange,

    /// <summary>Confidence increased.</summary>
    ConfidenceIncrease,

    /// <summary>Confidence decreased.</summary>
    ConfidenceDecrease,

    /// <summary>Fact was confirmed.</summary>
    Confirmed,

    /// <summary>Fact was invalidated.</summary>
    Invalidated
}

/// <summary>
/// Summary of diff statistics.
/// </summary>
public class DiffSummary
{
    /// <summary>
    /// Number of facts added.
    /// </summary>
    public int AddedCount { get; set; }

    /// <summary>
    /// Number of facts removed.
    /// </summary>
    public int RemovedCount { get; set; }

    /// <summary>
    /// Number of facts modified.
    /// </summary>
    public int ModifiedCount { get; set; }

    /// <summary>
    /// Total changes.
    /// </summary>
    public int TotalChanges => AddedCount + RemovedCount + ModifiedCount;

    /// <summary>
    /// Changes by category.
    /// </summary>
    public IReadOnlyDictionary<SemanticStoreCategory, int> ByCategory { get; set; } =
        new Dictionary<SemanticStoreCategory, int>();

    /// <summary>
    /// Net fact count change.
    /// </summary>
    public int NetChange => AddedCount - RemovedCount;
}
