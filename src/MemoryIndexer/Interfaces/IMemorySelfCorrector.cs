using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Memory Self-Correction service for autonomous memory quality improvement.
/// Detects and corrects issues in stored memories.
/// </summary>
/// <remarks>
/// Research basis: A-MEM's self-evolution, MemR³'s evidence-gap tracking.
/// Key capabilities:
/// - Contradiction detection and resolution
/// - Outdated information identification
/// - Missing information tracking
/// - Confidence decay management
/// </remarks>
public interface IMemorySelfCorrector
{
    /// <summary>
    /// Analyzes memories for potential issues.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Analysis options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with detected issues.</returns>
    Task<MemoryAnalysisResult> AnalyzeMemoriesAsync(
        string userId,
        MemoryAnalysisOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects contradictions between memories.
    /// </summary>
    /// <param name="memories">Memories to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detected contradictions.</returns>
    Task<IReadOnlyList<MemoryContradiction>> DetectContradictionsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies potentially outdated memories.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Detection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Outdated memories with reasons.</returns>
    Task<IReadOnlyList<OutdatedMemory>> IdentifyOutdatedMemoriesAsync(
        string userId,
        OutdatedDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks evidence gaps in memory knowledge.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="query">Query context for gap detection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Identified evidence gaps.</returns>
    Task<IReadOnlyList<EvidenceGap>> TrackEvidenceGapsAsync(
        string userId,
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies corrections to memories.
    /// </summary>
    /// <param name="corrections">Corrections to apply.</param>
    /// <param name="options">Correction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Correction result.</returns>
    Task<CorrectionResult> ApplyCorrectionsAsync(
        IReadOnlyList<MemoryCorrection> corrections,
        CorrectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a contradiction between memories.
    /// </summary>
    /// <param name="contradiction">The contradiction to resolve.</param>
    /// <param name="strategy">Resolution strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result.</returns>
    Task<ContradictionResolution> ResolveContradictionAsync(
        MemoryContradiction contradiction,
        ResolutionStrategy strategy = ResolutionStrategy.KeepNewest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates confidence scores based on evidence and time decay.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Confidence update options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    Task<ConfidenceUpdateResult> UpdateConfidenceScoresAsync(
        string userId,
        ConfidenceUpdateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets correction history for audit purposes.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="limit">Maximum records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Correction history.</returns>
    Task<IReadOnlyList<CorrectionRecord>> GetCorrectionHistoryAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

#region Analysis Types

/// <summary>
/// Memory analysis options.
/// </summary>
public sealed class MemoryAnalysisOptions
{
    /// <summary>
    /// Whether to check for contradictions.
    /// </summary>
    public bool CheckContradictions { get; init; } = true;

    /// <summary>
    /// Whether to check for outdated information.
    /// </summary>
    public bool CheckOutdated { get; init; } = true;

    /// <summary>
    /// Whether to check for duplicates.
    /// </summary>
    public bool CheckDuplicates { get; init; } = true;

    /// <summary>
    /// Whether to track evidence gaps.
    /// </summary>
    public bool TrackGaps { get; init; } = true;

    /// <summary>
    /// Minimum confidence threshold for analysis.
    /// </summary>
    public float MinConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Maximum memories to analyze.
    /// </summary>
    public int MaxMemoriesToAnalyze { get; init; } = 1000;

    /// <summary>
    /// Focus area for analysis (optional).
    /// </summary>
    public string? FocusQuery { get; init; }
}

/// <summary>
/// Result of memory analysis.
/// </summary>
public sealed class MemoryAnalysisResult
{
    /// <summary>
    /// Total memories analyzed.
    /// </summary>
    public int MemoriesAnalyzed { get; set; }

    /// <summary>
    /// Detected contradictions.
    /// </summary>
    public IReadOnlyList<MemoryContradiction> Contradictions { get; set; } = [];

    /// <summary>
    /// Outdated memories found.
    /// </summary>
    public IReadOnlyList<OutdatedMemory> OutdatedMemories { get; set; } = [];

    /// <summary>
    /// Duplicate memories found.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> DuplicateGroups { get; set; } = [];

    /// <summary>
    /// Evidence gaps identified.
    /// </summary>
    public IReadOnlyList<EvidenceGap> EvidenceGaps { get; set; } = [];

    /// <summary>
    /// Overall memory health score (0-1).
    /// </summary>
    public float HealthScore { get; set; }

    /// <summary>
    /// Suggested corrections.
    /// </summary>
    public IReadOnlyList<MemoryCorrection> SuggestedCorrections { get; set; } = [];

    /// <summary>
    /// Analysis duration.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Contradiction between memories.
/// </summary>
public sealed class MemoryContradiction
{
    /// <summary>
    /// Unique identifier for this contradiction.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// First conflicting memory.
    /// </summary>
    public required MemoryUnit Memory1 { get; init; }

    /// <summary>
    /// Second conflicting memory.
    /// </summary>
    public required MemoryUnit Memory2 { get; init; }

    /// <summary>
    /// Type of contradiction.
    /// </summary>
    public ContradictionType Type { get; set; }

    /// <summary>
    /// Description of the contradiction.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Confidence that this is a true contradiction (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Specific conflicting claims.
    /// </summary>
    public IReadOnlyList<ConflictingClaim> ConflictingClaims { get; set; } = [];

    /// <summary>
    /// When the contradiction was detected.
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Specific conflicting claim.
/// </summary>
public sealed class ConflictingClaim
{
    /// <summary>
    /// Claim from first memory.
    /// </summary>
    public string Claim1 { get; set; } = string.Empty;

    /// <summary>
    /// Claim from second memory.
    /// </summary>
    public string Claim2 { get; set; } = string.Empty;

    /// <summary>
    /// Why these claims conflict.
    /// </summary>
    public string ConflictReason { get; set; } = string.Empty;
}

/// <summary>
/// Outdated memory information.
/// </summary>
public sealed class OutdatedMemory
{
    /// <summary>
    /// The outdated memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Reason why it's considered outdated.
    /// </summary>
    public OutdatedReason Reason { get; set; }

    /// <summary>
    /// Detailed explanation.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Confidence that it's outdated (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Newer memory that supersedes this one (if any).
    /// </summary>
    public Guid? SupersededBy { get; set; }

    /// <summary>
    /// Suggested action.
    /// </summary>
    public OutdatedAction SuggestedAction { get; set; }
}

/// <summary>
/// Evidence gap in knowledge.
/// </summary>
public sealed class EvidenceGap
{
    /// <summary>
    /// Unique identifier for this gap.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Description of missing information.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Related entities that have incomplete information.
    /// </summary>
    public IReadOnlyList<string> RelatedEntities { get; set; } = [];

    /// <summary>
    /// Related memories that partially address this.
    /// </summary>
    public IReadOnlyList<Guid> PartialMemories { get; set; } = [];

    /// <summary>
    /// Importance of filling this gap (0-1).
    /// </summary>
    public float Importance { get; set; }

    /// <summary>
    /// Suggested queries to fill the gap.
    /// </summary>
    public IReadOnlyList<string> SuggestedQueries { get; set; } = [];

    /// <summary>
    /// When the gap was identified.
    /// </summary>
    public DateTime IdentifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Group of duplicate memories.
/// </summary>
public sealed class DuplicateGroup
{
    /// <summary>
    /// Memories in this duplicate group.
    /// </summary>
    public IReadOnlyList<Guid> MemoryIds { get; set; } = [];

    /// <summary>
    /// Similarity score between duplicates (0-1).
    /// </summary>
    public float Similarity { get; set; }

    /// <summary>
    /// Recommended canonical memory to keep.
    /// </summary>
    public Guid? RecommendedCanonical { get; set; }

    /// <summary>
    /// Reason for recommendation.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

#endregion

#region Correction Types

/// <summary>
/// Correction to apply to a memory.
/// </summary>
public sealed class MemoryCorrection
{
    /// <summary>
    /// Unique identifier for this correction.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Target memory ID.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Type of correction.
    /// </summary>
    public CorrectionType Type { get; set; }

    /// <summary>
    /// Original content (for reference).
    /// </summary>
    public string? OriginalContent { get; set; }

    /// <summary>
    /// Corrected content (if applicable).
    /// </summary>
    public string? CorrectedContent { get; set; }

    /// <summary>
    /// Reason for the correction.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// New confidence score (if updating).
    /// </summary>
    public float? NewConfidence { get; set; }

    /// <summary>
    /// Source of the correction (e.g., "contradiction resolution", "time decay").
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Priority of the correction.
    /// </summary>
    public CorrectionPriority Priority { get; set; }
}

/// <summary>
/// Options for applying corrections.
/// </summary>
public sealed class CorrectionOptions
{
    /// <summary>
    /// Whether to create backup before correction.
    /// </summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>
    /// Whether to validate corrections before applying.
    /// </summary>
    public bool ValidateBeforeApply { get; init; } = true;

    /// <summary>
    /// Maximum corrections to apply in one batch.
    /// </summary>
    public int MaxCorrectionsPerBatch { get; init; } = 100;

    /// <summary>
    /// Whether to record correction history.
    /// </summary>
    public bool RecordHistory { get; init; } = true;

    /// <summary>
    /// Only apply corrections above this priority.
    /// </summary>
    public CorrectionPriority MinPriority { get; init; } = CorrectionPriority.Low;
}

/// <summary>
/// Result of applying corrections.
/// </summary>
public sealed class CorrectionResult
{
    /// <summary>
    /// Whether corrections were successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Corrections that were applied.
    /// </summary>
    public IReadOnlyList<AppliedCorrection> AppliedCorrections { get; set; } = [];

    /// <summary>
    /// Corrections that failed.
    /// </summary>
    public IReadOnlyList<FailedCorrection> FailedCorrections { get; set; } = [];

    /// <summary>
    /// Corrections that were skipped.
    /// </summary>
    public IReadOnlyList<SkippedCorrection> SkippedCorrections { get; set; } = [];

    /// <summary>
    /// Total corrections processed.
    /// </summary>
    public int TotalProcessed { get; set; }

    /// <summary>
    /// Duration of correction process.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Applied correction record.
/// </summary>
public sealed class AppliedCorrection
{
    /// <summary>
    /// The correction that was applied.
    /// </summary>
    public required MemoryCorrection Correction { get; init; }

    /// <summary>
    /// When it was applied.
    /// </summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Backup reference (if created).
    /// </summary>
    public Guid? BackupId { get; set; }
}

/// <summary>
/// Failed correction record.
/// </summary>
public sealed class FailedCorrection
{
    /// <summary>
    /// The correction that failed.
    /// </summary>
    public required MemoryCorrection Correction { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Skipped correction record.
/// </summary>
public sealed class SkippedCorrection
{
    /// <summary>
    /// The correction that was skipped.
    /// </summary>
    public required MemoryCorrection Correction { get; init; }

    /// <summary>
    /// Reason for skipping.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Contradiction resolution result.
/// </summary>
public sealed class ContradictionResolution
{
    /// <summary>
    /// Whether resolution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The contradiction that was resolved.
    /// </summary>
    public required MemoryContradiction Contradiction { get; init; }

    /// <summary>
    /// Strategy used for resolution.
    /// </summary>
    public ResolutionStrategy Strategy { get; set; }

    /// <summary>
    /// Action taken.
    /// </summary>
    public ResolutionAction Action { get; set; }

    /// <summary>
    /// Memory that was kept (if applicable).
    /// </summary>
    public Guid? KeptMemoryId { get; set; }

    /// <summary>
    /// Memory that was modified (if applicable).
    /// </summary>
    public Guid? ModifiedMemoryId { get; set; }

    /// <summary>
    /// Memory that was archived/deleted (if applicable).
    /// </summary>
    public Guid? RemovedMemoryId { get; set; }

    /// <summary>
    /// New merged memory (if created).
    /// </summary>
    public Guid? MergedMemoryId { get; set; }

    /// <summary>
    /// Explanation of the resolution.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// Confidence update options.
/// </summary>
public sealed class ConfidenceUpdateOptions
{
    /// <summary>
    /// Apply time-based decay.
    /// </summary>
    public bool ApplyTimeDecay { get; init; } = true;

    /// <summary>
    /// Half-life for time decay in days.
    /// </summary>
    public int DecayHalfLifeDays { get; init; } = 30;

    /// <summary>
    /// Minimum confidence after decay.
    /// </summary>
    public float MinConfidenceAfterDecay { get; init; } = 0.1f;

    /// <summary>
    /// Boost confidence for frequently accessed memories.
    /// </summary>
    public bool BoostFrequentlyAccessed { get; init; } = true;

    /// <summary>
    /// Reduce confidence for contradicted memories.
    /// </summary>
    public bool ReduceForContradictions { get; init; } = true;
}

/// <summary>
/// Result of confidence update.
/// </summary>
public sealed class ConfidenceUpdateResult
{
    /// <summary>
    /// Memories whose confidence was updated.
    /// </summary>
    public int MemoriesUpdated { get; set; }

    /// <summary>
    /// Average confidence before update.
    /// </summary>
    public float AverageConfidenceBefore { get; set; }

    /// <summary>
    /// Average confidence after update.
    /// </summary>
    public float AverageConfidenceAfter { get; set; }

    /// <summary>
    /// Memories that fell below threshold.
    /// </summary>
    public IReadOnlyList<Guid> LowConfidenceMemories { get; set; } = [];
}

/// <summary>
/// Correction history record.
/// </summary>
public sealed class CorrectionRecord
{
    /// <summary>
    /// Record ID.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Memory that was corrected.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Type of correction.
    /// </summary>
    public CorrectionType Type { get; set; }

    /// <summary>
    /// Original value.
    /// </summary>
    public string? OriginalValue { get; set; }

    /// <summary>
    /// New value.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Reason for correction.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// When the correction was made.
    /// </summary>
    public DateTime CorrectedAt { get; set; }

    /// <summary>
    /// Source of correction.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Outdated detection options.
/// </summary>
public sealed class OutdatedDetectionOptions
{
    /// <summary>
    /// Maximum age in days before considering outdated.
    /// </summary>
    public int MaxAgeDays { get; init; } = 90;

    /// <summary>
    /// Check for superseding newer memories.
    /// </summary>
    public bool CheckForSuperseding { get; init; } = true;

    /// <summary>
    /// Entity types to focus on.
    /// </summary>
    public IReadOnlyList<string>? FocusEntityTypes { get; init; }

    /// <summary>
    /// Minimum confidence to consider for outdated check.
    /// </summary>
    public float MinConfidence { get; init; } = 0.3f;
}

#endregion

#region Enums

/// <summary>
/// Types of contradictions.
/// </summary>
public enum ContradictionType
{
    /// <summary>Direct factual contradiction.</summary>
    Factual,
    /// <summary>Temporal inconsistency.</summary>
    Temporal,
    /// <summary>Logical inconsistency.</summary>
    Logical,
    /// <summary>Attribute conflict.</summary>
    Attribute,
    /// <summary>Relationship conflict.</summary>
    Relationship
}

/// <summary>
/// Reasons for memory being outdated.
/// </summary>
public enum OutdatedReason
{
    /// <summary>Age-based expiration.</summary>
    Age,
    /// <summary>Superseded by newer information.</summary>
    Superseded,
    /// <summary>Referenced entity no longer exists.</summary>
    EntityObsolete,
    /// <summary>Information explicitly invalidated.</summary>
    Invalidated,
    /// <summary>Low confidence due to decay.</summary>
    ConfidenceDecay
}

/// <summary>
/// Actions for outdated memories.
/// </summary>
public enum OutdatedAction
{
    /// <summary>Archive the memory.</summary>
    Archive,
    /// <summary>Delete the memory.</summary>
    Delete,
    /// <summary>Update the memory.</summary>
    Update,
    /// <summary>Merge with newer memory.</summary>
    Merge,
    /// <summary>Flag for review.</summary>
    FlagForReview
}

/// <summary>
/// Types of corrections.
/// </summary>
public enum CorrectionType
{
    /// <summary>Content update.</summary>
    ContentUpdate,
    /// <summary>Confidence adjustment.</summary>
    ConfidenceAdjustment,
    /// <summary>Type change.</summary>
    TypeChange,
    /// <summary>Tier change.</summary>
    TierChange,
    /// <summary>Metadata update.</summary>
    MetadataUpdate,
    /// <summary>Archive.</summary>
    Archive,
    /// <summary>Delete.</summary>
    Delete,
    /// <summary>Merge with another memory.</summary>
    Merge
}

/// <summary>
/// Correction priority levels.
/// </summary>
public enum CorrectionPriority
{
    /// <summary>Low priority, can be deferred.</summary>
    Low,
    /// <summary>Normal priority.</summary>
    Normal,
    /// <summary>High priority, should be applied soon.</summary>
    High,
    /// <summary>Critical, apply immediately.</summary>
    Critical
}

/// <summary>
/// Strategies for resolving contradictions.
/// </summary>
public enum ResolutionStrategy
{
    /// <summary>Keep the newest memory.</summary>
    KeepNewest,
    /// <summary>Keep the oldest memory.</summary>
    KeepOldest,
    /// <summary>Keep the higher confidence memory.</summary>
    KeepHigherConfidence,
    /// <summary>Keep the more frequently accessed memory.</summary>
    KeepMostAccessed,
    /// <summary>Merge both memories.</summary>
    Merge,
    /// <summary>Mark both as uncertain.</summary>
    MarkUncertain,
    /// <summary>Flag for manual review.</summary>
    ManualReview
}

/// <summary>
/// Actions taken during resolution.
/// </summary>
public enum ResolutionAction
{
    /// <summary>Kept one memory, archived the other.</summary>
    KeptAndArchived,
    /// <summary>Kept one memory, deleted the other.</summary>
    KeptAndDeleted,
    /// <summary>Merged both memories.</summary>
    Merged,
    /// <summary>Updated confidence on both.</summary>
    UpdatedConfidence,
    /// <summary>Flagged for manual review.</summary>
    FlaggedForReview,
    /// <summary>No action taken.</summary>
    NoAction
}

#endregion
