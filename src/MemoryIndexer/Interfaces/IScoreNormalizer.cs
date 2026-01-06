using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for normalizing memory scores to improve distribution.
/// Phase 21.2: Score Distribution Normalization.
/// </summary>
public interface IScoreNormalizer
{
    /// <summary>
    /// Normalizes scores to improve distribution and ranking.
    /// </summary>
    /// <param name="scoredMemories">The memories with raw scores to normalize.</param>
    /// <returns>Memories with normalized scores.</returns>
    IReadOnlyList<NormalizableMemory> Normalize(IReadOnlyList<NormalizableMemory> scoredMemories);

    /// <summary>
    /// Gets normalization statistics for monitoring.
    /// </summary>
    /// <returns>Statistics about the normalization process.</returns>
    NormalizationStats GetStats();
}

/// <summary>
/// Memory with both raw and normalized scores for normalization processing.
/// </summary>
public sealed class NormalizableMemory
{
    /// <summary>
    /// The memory unit.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Raw score before normalization.
    /// </summary>
    public float RawScore { get; set; }

    /// <summary>
    /// Normalized score (typically 0.0-1.0 range).
    /// </summary>
    public float NormalizedScore { get; set; }
}

/// <summary>
/// Statistics about score normalization.
/// </summary>
public sealed class NormalizationStats
{
    /// <summary>
    /// Original score range before normalization.
    /// </summary>
    public float OriginalSpread { get; set; }

    /// <summary>
    /// Normalized score range after normalization.
    /// </summary>
    public float NormalizedSpread { get; set; }

    /// <summary>
    /// Mean of original scores.
    /// </summary>
    public float OriginalMean { get; set; }

    /// <summary>
    /// Standard deviation of original scores.
    /// </summary>
    public float OriginalStdDev { get; set; }

    /// <summary>
    /// Normalization strategy used.
    /// </summary>
    public NormalizationStrategy Strategy { get; set; }
}

/// <summary>
/// Score normalization strategies.
/// </summary>
public enum NormalizationStrategy
{
    /// <summary>
    /// No normalization (current behavior).
    /// </summary>
    None,

    /// <summary>
    /// Min-max scaling to 0-1 range.
    /// </summary>
    MinMax,

    /// <summary>
    /// Percentile-based ranking.
    /// </summary>
    Percentile,

    /// <summary>
    /// Z-score standardization.
    /// </summary>
    ZScore,

    /// <summary>
    /// Adaptive strategy based on distribution.
    /// </summary>
    Adaptive
}
