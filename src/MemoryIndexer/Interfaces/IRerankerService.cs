namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for re-ranking search results using cross-encoder models.
/// </summary>
/// <remarks>
/// Vector search provides high recall (finds many relevant results) but lower precision.
/// Re-ranking with cross-encoder models provides high precision by computing
/// query-document relevance scores directly.
///
/// Typical pipeline: Query → Embedder → Vector Search (Top 20) → Reranker → Final (Top 5)
/// </remarks>
public interface IRerankerService
{
    /// <summary>
    /// Re-rank memories based on semantic relevance to query using cross-encoder scoring.
    /// </summary>
    /// <typeparam name="TMetadata">Type of metadata to preserve through re-ranking.</typeparam>
    /// <param name="query">The search query.</param>
    /// <param name="candidates">Candidate memories from initial search.</param>
    /// <param name="topK">Number of top results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Re-ranked memories ordered by relevance score.</returns>
    Task<IReadOnlyList<RerankResult<TMetadata>>> RerankAsync<TMetadata>(
        string query,
        IReadOnlyList<RerankCandidate<TMetadata>> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute relevance score between a query and a single document.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="document">The document content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relevance score (0.0 - 1.0).</returns>
    Task<float> ScoreAsync(
        string query,
        string document,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Candidate for re-ranking with typed metadata.
/// </summary>
/// <typeparam name="TMetadata">Type of metadata to preserve through re-ranking.</typeparam>
public record RerankCandidate<TMetadata>
{
    /// <summary>
    /// Document content to score against query.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Original score from vector search.
    /// </summary>
    public float OriginalScore { get; init; }

    /// <summary>
    /// Associated memory ID for correlation.
    /// </summary>
    public Guid? MemoryId { get; init; }

    /// <summary>
    /// Typed metadata to preserve through re-ranking.
    /// </summary>
    public TMetadata? Metadata { get; init; }
}

/// <summary>
/// Result from re-ranking with typed metadata.
/// </summary>
/// <typeparam name="TMetadata">Type of metadata preserved from candidate.</typeparam>
public record RerankResult<TMetadata>
{
    /// <summary>
    /// Index of the candidate in original list.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Re-ranked relevance score from cross-encoder.
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// Original score from vector search.
    /// </summary>
    public float OriginalScore { get; init; }

    /// <summary>
    /// Content that was re-ranked.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Associated memory ID if provided.
    /// </summary>
    public Guid? MemoryId { get; init; }

    /// <summary>
    /// Typed metadata preserved from candidate.
    /// </summary>
    public TMetadata? Metadata { get; init; }
}
