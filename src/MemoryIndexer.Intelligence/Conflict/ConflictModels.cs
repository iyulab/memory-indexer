using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.Conflict;

/// <summary>
/// Represents the result of contradiction detection analysis.
/// </summary>
public sealed class ContradictionAnalysis
{
    /// <summary>
    /// Whether a contradiction was detected.
    /// </summary>
    public bool HasContradiction { get; init; }

    /// <summary>
    /// The new memory/triple being analyzed.
    /// </summary>
    public required object NewItem { get; init; }

    /// <summary>
    /// The existing item that conflicts with the new one.
    /// </summary>
    public object? ConflictingItem { get; init; }

    /// <summary>
    /// Confidence score of the contradiction detection (0.0 to 1.0).
    /// </summary>
    public float ContradictionConfidence { get; init; }

    /// <summary>
    /// Human-readable description of the conflict.
    /// </summary>
    public string ConflictDescription { get; init; } = string.Empty;

    /// <summary>
    /// The type of contradiction detected.
    /// </summary>
    public ContradictionType Type { get; init; }

    /// <summary>
    /// Additional context about the contradiction.
    /// </summary>
    public Dictionary<string, string> Context { get; init; } = [];
}

/// <summary>
/// Types of contradictions that can be detected.
/// </summary>
public enum ContradictionType
{
    /// <summary>
    /// No contradiction detected.
    /// </summary>
    None = 0,

    /// <summary>
    /// Direct factual contradiction (e.g., "A is B" vs "A is C").
    /// </summary>
    Factual = 1,

    /// <summary>
    /// Temporal contradiction (overlapping valid time periods with different values).
    /// </summary>
    Temporal = 2,

    /// <summary>
    /// Preference contradiction (conflicting user preferences).
    /// </summary>
    Preference = 3,

    /// <summary>
    /// Semantic contradiction detected via NLI or embedding similarity.
    /// </summary>
    Semantic = 4,

    /// <summary>
    /// Logical contradiction (mutually exclusive statements).
    /// </summary>
    Logical = 5
}

/// <summary>
/// Strategies for resolving contradictions.
/// </summary>
public enum ResolutionStrategy
{
    /// <summary>
    /// Keep the more recent information, supersede the old.
    /// </summary>
    RecencyFirst = 0,

    /// <summary>
    /// Keep the information with higher confidence score.
    /// </summary>
    ConfidenceFirst = 1,

    /// <summary>
    /// Keep the information from a more authoritative source.
    /// </summary>
    SourceAuthority = 2,

    /// <summary>
    /// Ask the user to resolve the contradiction.
    /// </summary>
    AskUser = 3,

    /// <summary>
    /// Keep both pieces of information with a CONTRADICTS relation.
    /// </summary>
    KeepBoth = 4,

    /// <summary>
    /// Use temporal validity - keep both but mark time periods.
    /// </summary>
    TemporalPartition = 5
}

/// <summary>
/// Result of contradiction resolution.
/// </summary>
public sealed class ResolutionResult
{
    /// <summary>
    /// Whether the resolution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The strategy that was applied.
    /// </summary>
    public ResolutionStrategy AppliedStrategy { get; init; }

    /// <summary>
    /// The item that was kept/preferred.
    /// </summary>
    public object? KeptItem { get; init; }

    /// <summary>
    /// The item that was superseded/archived.
    /// </summary>
    public object? SupersededItem { get; init; }

    /// <summary>
    /// If a new merged item was created.
    /// </summary>
    public object? MergedItem { get; init; }

    /// <summary>
    /// Human-readable explanation of the resolution.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>
    /// Whether user intervention is required.
    /// </summary>
    public bool RequiresUserIntervention { get; init; }

    /// <summary>
    /// Question to ask the user if intervention is required.
    /// </summary>
    public string? UserQuestion { get; init; }
}

/// <summary>
/// Options for contradiction detection.
/// All detection methods (semantic, temporal, rule-based) are always enabled.
/// </summary>
public sealed class ContradictionDetectionOptions
{
    /// <summary>
    /// Minimum semantic similarity threshold to consider items related (default: 0.7).
    /// Higher values = stricter topic matching before contradiction check.
    /// </summary>
    public float SimilarityThreshold { get; init; } = 0.7f;

    /// <summary>
    /// Minimum confidence to report a contradiction (default: 0.6).
    /// </summary>
    public float MinContradictionConfidence { get; init; } = 0.6f;

    /// <summary>
    /// Maximum number of existing items to compare against (default: 100).
    /// </summary>
    public int MaxComparisonItems { get; init; } = 100;

    /// <summary>
    /// Point-in-time for temporal queries (default: now).
    /// </summary>
    public DateTime? AsOfDate { get; init; }
}
