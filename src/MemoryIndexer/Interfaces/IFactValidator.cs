namespace MemoryIndexer.Interfaces;

/// <summary>
/// Interface for validating facts against existing facts with category-specific rules.
/// Phase v0.9.2: Fact Conflict Resolution.
/// </summary>
public interface IFactValidator
{
    /// <summary>
    /// Validates a new fact against existing facts.
    /// Applies category-specific resolution rules.
    /// </summary>
    /// <param name="newFact">The new fact to validate.</param>
    /// <param name="existingFacts">Existing facts to compare against.</param>
    /// <param name="options">Optional validation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with recommended action.</returns>
    Task<FactConflictAnalysis> ValidateAsync(
        UserFact newFact,
        IReadOnlyList<SemanticStoreEntry> existingFacts,
        FactValidationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects conflicts between a new fact and existing facts.
    /// </summary>
    /// <param name="newFact">The new fact to check.</param>
    /// <param name="existingFacts">Existing facts to compare against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of detected conflicts.</returns>
    Task<IReadOnlyList<FactConflict>> DetectConflictsAsync(
        UserFact newFact,
        IReadOnlyList<SemanticStoreEntry> existingFacts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a conflict based on category-specific rules.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <param name="strategy">Optional override strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result with action to take.</returns>
    Task<FactResolutionResult> ResolveConflictAsync(
        FactConflict conflict,
        FactResolutionStrategy? strategy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default resolution strategy for a category.
    /// </summary>
    /// <param name="category">The fact category.</param>
    /// <returns>The default strategy for this category.</returns>
    CategoryResolutionRule GetCategoryRule(FactCategory category);
}

#region Models

/// <summary>
/// Options for fact validation.
/// </summary>
public sealed class FactValidationOptions
{
    /// <summary>
    /// Confidence differential required for auto-resolution.
    /// If new fact confidence exceeds existing by this threshold, auto-replace.
    /// Default: 0.2
    /// </summary>
    public float ConfidenceDifferentialThreshold { get; init; } = 0.2f;

    /// <summary>
    /// Semantic similarity threshold for conflict detection.
    /// Default: 0.8
    /// </summary>
    public float SimilarityThreshold { get; init; } = 0.8f;

    /// <summary>
    /// Whether to allow automatic resolution without user confirmation.
    /// Default: true for non-Identity categories
    /// </summary>
    public bool AllowAutoResolution { get; init; } = true;

    /// <summary>
    /// Whether to check SPO (Subject-Predicate-Object) triple matching.
    /// Default: true
    /// </summary>
    public bool UseSpoMatching { get; init; } = true;

    /// <summary>
    /// Maximum existing facts to compare against.
    /// Default: 50
    /// </summary>
    public int MaxComparisonFacts { get; init; } = 50;
}

/// <summary>
/// Result of fact conflict analysis.
/// </summary>
public sealed class FactConflictAnalysis
{
    /// <summary>
    /// Whether the new fact is valid (no blocking conflicts).
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Detected conflicts (if any).
    /// </summary>
    public IReadOnlyList<FactConflict> Conflicts { get; init; } = [];

    /// <summary>
    /// Recommended action for the new fact.
    /// </summary>
    public FactConflictAction RecommendedAction { get; init; }

    /// <summary>
    /// Resolution strategy that was applied.
    /// </summary>
    public FactResolutionStrategy? AppliedStrategy { get; init; }

    /// <summary>
    /// Whether user confirmation is required.
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>
    /// Question to ask user if confirmation required.
    /// </summary>
    public string? ConfirmationQuestion { get; init; }

    /// <summary>
    /// Overall confidence in the analysis.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Explanation of the analysis result.
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Create a valid result (no conflicts).
    /// </summary>
    public static FactConflictAnalysis Valid(float confidence = 1.0f) => new()
    {
        IsValid = true,
        RecommendedAction = FactConflictAction.Add,
        Confidence = confidence,
        Reasoning = "No conflicts detected"
    };

    /// <summary>
    /// Create a duplicate result.
    /// </summary>
    public static FactConflictAnalysis Duplicate(SemanticStoreEntry existing) => new()
    {
        IsValid = false,
        Conflicts = [new FactConflict { ConflictType = FactConflictType.Duplicate, ExistingFact = existing }],
        RecommendedAction = FactConflictAction.Skip,
        Confidence = 1.0f,
        Reasoning = "Exact duplicate exists"
    };
}

/// <summary>
/// A detected conflict between facts.
/// </summary>
public sealed class FactConflict
{
    /// <summary>
    /// Type of conflict detected.
    /// </summary>
    public FactConflictType ConflictType { get; init; }

    /// <summary>
    /// The existing fact that conflicts.
    /// </summary>
    public SemanticStoreEntry? ExistingFact { get; init; }

    /// <summary>
    /// Confidence in the conflict detection (0-1).
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Semantic similarity score (if applicable).
    /// </summary>
    public float? SimilarityScore { get; init; }

    /// <summary>
    /// Whether SPO triple matches.
    /// </summary>
    public bool? SpoMatch { get; init; }

    /// <summary>
    /// Detailed description of the conflict.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Category of the conflicting facts.
    /// </summary>
    public FactCategory Category { get; init; }
}

/// <summary>
/// Result of conflict resolution.
/// </summary>
public sealed class FactResolutionResult
{
    /// <summary>
    /// Whether resolution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Action to take.
    /// </summary>
    public FactConflictAction Action { get; init; }

    /// <summary>
    /// Strategy that was applied.
    /// </summary>
    public FactResolutionStrategy AppliedStrategy { get; init; }

    /// <summary>
    /// The entry to keep (if any).
    /// </summary>
    public SemanticStoreEntry? KeptEntry { get; init; }

    /// <summary>
    /// The entry that was archived (if any).
    /// </summary>
    public SemanticStoreEntry? ArchivedEntry { get; init; }

    /// <summary>
    /// Merged content (if action is Merge).
    /// </summary>
    public string? MergedContent { get; init; }

    /// <summary>
    /// Explanation of the resolution.
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Whether this resolution requires user confirmation.
    /// </summary>
    public bool RequiresUserAction { get; init; }
}

/// <summary>
/// Resolution strategies for fact conflicts.
/// </summary>
public enum FactResolutionStrategy
{
    /// <summary>
    /// Keep the newer fact, archive the old one.
    /// </summary>
    RecencyFirst,

    /// <summary>
    /// Keep the fact with higher confidence.
    /// </summary>
    ConfidenceFirst,

    /// <summary>
    /// Keep the fact with more confirmations.
    /// </summary>
    ConfirmationFirst,

    /// <summary>
    /// Archive old fact, add new as current version.
    /// </summary>
    TemporalPartition,

    /// <summary>
    /// Keep both facts (no automatic resolution).
    /// </summary>
    KeepBoth,

    /// <summary>
    /// Require user confirmation before any change.
    /// </summary>
    RequireConfirmation,

    /// <summary>
    /// Auto-select based on category and confidence differential.
    /// </summary>
    Auto
}

/// <summary>
/// Category-specific resolution rules.
/// </summary>
public sealed class CategoryResolutionRule
{
    /// <summary>
    /// The fact category this rule applies to.
    /// </summary>
    public FactCategory Category { get; init; }

    /// <summary>
    /// Default resolution strategy for this category.
    /// </summary>
    public FactResolutionStrategy DefaultStrategy { get; init; }

    /// <summary>
    /// Minimum confidence differential for auto-resolution.
    /// Higher values require greater confidence difference.
    /// </summary>
    public float MinConfidenceDifferential { get; init; }

    /// <summary>
    /// Whether changes require explicit user confirmation.
    /// </summary>
    public bool RequiresExplicitConfirmation { get; init; }

    /// <summary>
    /// Whether to preserve history (archive old facts).
    /// </summary>
    public bool PreserveHistory { get; init; }

    /// <summary>
    /// Description of this rule.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Default rules for each category.
    /// </summary>
    public static readonly IReadOnlyDictionary<FactCategory, CategoryResolutionRule> Defaults =
        new Dictionary<FactCategory, CategoryResolutionRule>
        {
            [FactCategory.Identity] = new()
            {
                Category = FactCategory.Identity,
                DefaultStrategy = FactResolutionStrategy.RequireConfirmation,
                MinConfidenceDifferential = 0.3f, // Stricter threshold
                RequiresExplicitConfirmation = true,
                PreserveHistory = true,
                Description = "Identity facts (name, age) require explicit confirmation for changes"
            },
            [FactCategory.Preference] = new()
            {
                Category = FactCategory.Preference,
                DefaultStrategy = FactResolutionStrategy.RecencyFirst,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = false,
                Description = "Preferences can change; newer values take precedence"
            },
            [FactCategory.Relationship] = new()
            {
                Category = FactCategory.Relationship,
                DefaultStrategy = FactResolutionStrategy.TemporalPartition,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = true,
                Description = "Relationships evolve; preserve history with temporal partitioning"
            },
            [FactCategory.Location] = new()
            {
                Category = FactCategory.Location,
                DefaultStrategy = FactResolutionStrategy.TemporalPartition,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = true,
                Description = "Locations change over time; preserve history"
            },
            [FactCategory.Temporal] = new()
            {
                Category = FactCategory.Temporal,
                DefaultStrategy = FactResolutionStrategy.KeepBoth,
                MinConfidenceDifferential = 0.1f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = true,
                Description = "Temporal facts may have multiple valid values"
            },
            [FactCategory.Skill] = new()
            {
                Category = FactCategory.Skill,
                DefaultStrategy = FactResolutionStrategy.ConfidenceFirst,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = false,
                Description = "Skills can be refined; higher confidence wins"
            },
            [FactCategory.Goal] = new()
            {
                Category = FactCategory.Goal,
                DefaultStrategy = FactResolutionStrategy.RecencyFirst,
                MinConfidenceDifferential = 0.1f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = true,
                Description = "Goals change; preserve history for context"
            },
            [FactCategory.Health] = new()
            {
                Category = FactCategory.Health,
                DefaultStrategy = FactResolutionStrategy.RequireConfirmation,
                MinConfidenceDifferential = 0.3f,
                RequiresExplicitConfirmation = true,
                PreserveHistory = true,
                Description = "Health facts are critical; require confirmation"
            },
            [FactCategory.Professional] = new()
            {
                Category = FactCategory.Professional,
                DefaultStrategy = FactResolutionStrategy.TemporalPartition,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = true,
                Description = "Professional info changes over time; preserve history"
            },
            [FactCategory.General] = new()
            {
                Category = FactCategory.General,
                DefaultStrategy = FactResolutionStrategy.ConfidenceFirst,
                MinConfidenceDifferential = 0.2f,
                RequiresExplicitConfirmation = false,
                PreserveHistory = false,
                Description = "General facts; higher confidence wins"
            }
        };
}

#endregion
