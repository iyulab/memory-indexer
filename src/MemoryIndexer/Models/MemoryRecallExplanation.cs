using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Models;

/// <summary>
/// Explanation of why a memory was recalled and how it was scored.
/// Phase 23.3: Observability & Debugging Enhancement.
/// </summary>
public sealed class MemoryRecallExplanation
{
    /// <summary>
    /// The memory that was recalled.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Final combined score (0-1).
    /// </summary>
    public required float FinalScore { get; init; }

    /// <summary>
    /// Breakdown of score components.
    /// </summary>
    public required RecallScoreBreakdown ScoreComponents { get; init; }

    /// <summary>
    /// Tier from which memory was recalled.
    /// </summary>
    public required Tier SourceTier { get; init; }

    /// <summary>
    /// Reason for recall (semantic match, temporal proximity, etc.).
    /// </summary>
    public string? RecallReason { get; init; }

    /// <summary>
    /// Query intent that influenced recall.
    /// </summary>
    public QueryIntent? DetectedIntent { get; init; }

    /// <summary>
    /// Type boost applied (if any) from Phase 23.1.
    /// </summary>
    public float TypeBoost { get; init; }
}

/// <summary>
/// Detailed breakdown of scoring components for recall explanation.
/// </summary>
public sealed class RecallScoreBreakdown
{
    /// <summary>
    /// Semantic similarity score (0-1).
    /// Based on cosine similarity of embeddings.
    /// </summary>
    public float SemanticScore { get; init; }

    /// <summary>
    /// Recency score (0-1).
    /// Based on time since creation/access.
    /// </summary>
    public float RecencyScore { get; init; }

    /// <summary>
    /// Importance score (0-1).
    /// Based on classification importance.
    /// </summary>
    public float ImportanceScore { get; init; }

    /// <summary>
    /// Access frequency score (0-1).
    /// Based on number of recalls.
    /// </summary>
    public float FrequencyScore { get; init; }

    /// <summary>
    /// Keyword match boost (0-1).
    /// Based on BM25 or keyword overlap.
    /// </summary>
    public float KeywordBoost { get; init; }

    /// <summary>
    /// Type distribution boost (0-1).
    /// From Phase 23.1 balancer.
    /// </summary>
    public float TypeBoost { get; init; }

    /// <summary>
    /// Intent alignment score (0-1).
    /// How well memory matches query intent.
    /// </summary>
    public float IntentScore { get; init; }

    /// <summary>
    /// Explains how final score was calculated.
    /// </summary>
    public string? Explanation { get; init; }
}
