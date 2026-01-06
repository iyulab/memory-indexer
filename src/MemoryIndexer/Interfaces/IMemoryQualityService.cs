using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for analyzing memory quality metrics.
/// </summary>
public interface IMemoryQualityService
{
    /// <summary>
    /// Analyzes the quality of a memory unit.
    /// </summary>
    /// <param name="memory">The memory unit to analyze.</param>
    /// <param name="userId">The user ID for context comparison.</param>
    /// <param name="query">Optional query for relevance scoring.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quality metrics for the memory.</returns>
    Task<QualityMetrics> AnalyzeQualityAsync(
        MemoryUnit memory,
        string userId,
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch analyzes quality for multiple memories.
    /// </summary>
    /// <param name="memories">The memory units to analyze.</param>
    /// <param name="userId">The user ID for context comparison.</param>
    /// <param name="query">Optional query for relevance scoring.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quality metrics for each memory.</returns>
    Task<IReadOnlyList<QualityMetrics>> AnalyzeBatchQualityAsync(
        IReadOnlyList<MemoryUnit> memories,
        string userId,
        string? query = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Quality metrics for a memory unit.
/// </summary>
public sealed class QualityMetrics
{
    /// <summary>
    /// Memory ID that these metrics apply to.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Uniqueness score (0.0-1.0).
    /// 1.0 = completely unique, 0.0 = duplicate.
    /// Calculated as: 1 - (similarity with most similar memory)
    /// </summary>
    public required float UniquenessScore { get; init; }

    /// <summary>
    /// Relevance score (0.0-1.0).
    /// Measures semantic similarity to query (if provided).
    /// 1.0 = highly relevant, 0.0 = irrelevant.
    /// </summary>
    public float RelevanceScore { get; init; }

    /// <summary>
    /// Completeness score (0.0-1.0).
    /// Measures information completeness based on content length and detail.
    /// 1.0 = complete and detailed, 0.0 = minimal or vague.
    /// </summary>
    public required float CompletenessScore { get; init; }

    /// <summary>
    /// Consistency score (0.0-1.0).
    /// Measures logical consistency with other memories.
    /// 1.0 = fully consistent, 0.0 = contradictory.
    /// </summary>
    public required float ConsistencyScore { get; init; }

    /// <summary>
    /// Overall quality score (0.0-1.0).
    /// Weighted average of all metrics.
    /// Target: >0.8 for high quality.
    /// </summary>
    public required float OverallScore { get; init; }

    /// <summary>
    /// Optional quality issues detected.
    /// </summary>
    public List<string>? Issues { get; init; }
}
