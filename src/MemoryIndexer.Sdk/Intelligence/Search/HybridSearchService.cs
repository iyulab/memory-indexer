using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Search;

/// <summary>
/// Hybrid search service combining dense (vector) and sparse (BM25) retrieval.
/// Uses Reciprocal Rank Fusion (RRF) for score combination.
/// Supports HyDE (Hypothetical Document Embeddings) for improved query understanding.
/// </summary>
public sealed class HybridSearchService : IHybridSearchService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IHydeQueryExpander? _hydeExpander;
    private readonly BM25Index _bm25Index;
    private readonly ILogger<HybridSearchService> _logger;
    private readonly SearchOptions _options;

    public HybridSearchService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IOptions<MemoryIndexerOptions> options,
        ILogger<HybridSearchService> logger,
        IHydeQueryExpander? hydeExpander = null)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _hydeExpander = hydeExpander;
        _options = options.Value.Search;
        _logger = logger;
        _bm25Index = new BM25Index();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var denseWeight = options.DenseWeight ?? _options.DenseWeight;
        var sparseWeight = options.SparseWeight ?? _options.SparseWeight;
        var limit = options.Limit ?? _options.DefaultLimit;
        var rrfK = options.RrfK ?? _options.RrfK;

        // Determine if HyDE should be used
        var useHyde = options.UseHyde ?? _options.EnableHyde;
        var hydeDocCount = options.HydeDocumentCount ?? _options.HydeDocumentCount;

        _logger.LogDebug(
            "Hybrid search: query='{Query}', denseWeight={DenseWeight}, sparseWeight={SparseWeight}, hyde={UseHyde}",
            query.Length > 50 ? query[..50] + "..." : query,
            denseWeight, sparseWeight, useHyde);

        // Generate query embedding (with optional HyDE)
        var queryEmbedding = await GenerateQueryEmbeddingAsync(query, useHyde, hydeDocCount, cancellationToken);

        var denseSearchOptions = new MemorySearchOptions
        {
            UserId = options.UserId,
            SessionId = options.SessionId,
            Limit = limit * 3, // Over-fetch for fusion
            MinScore = options.MinScore ?? _options.MinScore,
            Types = options.Types,
            CreatedAfter = options.CreatedAfter,
            CreatedBefore = options.CreatedBefore,
            IncludeDeleted = options.IncludeDeleted
        };

        var denseResultsTask = _memoryStore.SearchAsync(queryEmbedding, denseSearchOptions, cancellationToken);
        var sparseResults = _bm25Index.Search(query, limit * 3);

        var denseResults = await denseResultsTask;

        _logger.LogDebug(
            "Search results: dense={DenseCount}, sparse={SparseCount}",
            denseResults.Count, sparseResults.Count);

        // Apply Reciprocal Rank Fusion (RRF)
        var fusedScores = new Dictionary<Guid, FusionScore>();

        // Add dense scores
        for (var rank = 0; rank < denseResults.Count; rank++)
        {
            var result = denseResults[rank];
            var rrfScore = 1.0 / (rrfK + rank + 1);

            fusedScores[result.Memory.Id] = new FusionScore
            {
                Memory = result.Memory,
                DenseScore = result.Score,
                DenseRrfScore = rrfScore * denseWeight,
                SparseScore = 0,
                SparseRrfScore = 0
            };
        }

        // Add sparse scores
        for (var rank = 0; rank < sparseResults.Count; rank++)
        {
            var (id, score) = sparseResults[rank];
            var rrfScore = 1.0 / (rrfK + rank + 1);

            if (fusedScores.TryGetValue(id, out var existing))
            {
                existing.SparseScore = score;
                existing.SparseRrfScore = rrfScore * sparseWeight;
            }
            else
            {
                // Need to fetch memory for sparse-only results
                var memory = await _memoryStore.GetByIdAsync(id, cancellationToken);
                if (memory != null)
                {
                    fusedScores[id] = new FusionScore
                    {
                        Memory = memory,
                        DenseScore = 0,
                        DenseRrfScore = 0,
                        SparseScore = score,
                        SparseRrfScore = rrfScore * sparseWeight
                    };
                }
            }
        }

        // Calculate final scores and apply MMR if requested
        var results = fusedScores.Values
            .OrderByDescending(f => f.TotalRrfScore)
            .Take(limit)
            .Select(f => new HybridSearchResult
            {
                Memory = f.Memory,
                Score = (float)f.TotalRrfScore,
                DenseScore = f.DenseScore,
                SparseScore = f.SparseScore,
                SearchType = (f.DenseScore > 0 && f.SparseScore > 0) ? SearchType.Hybrid
                    : f.DenseScore > 0 ? SearchType.Dense
                    : SearchType.Sparse
            })
            .ToList();

        // Apply MMR diversity filtering if enabled
        if (options.UseMmr == true)
        {
            results = ApplyMmrDiversity(results, options.MmrLambda ?? _options.MmrLambda);
        }

        _logger.LogDebug("Hybrid search returned {Count} results", results.Count);
        return results;
    }

    /// <inheritdoc />
    public void IndexDocument(Guid id, string content)
    {
        _bm25Index.AddDocument(id, content);
        _logger.LogDebug("Indexed document {Id} in BM25", id);
    }

    /// <inheritdoc />
    public void RemoveDocument(Guid id)
    {
        _bm25Index.RemoveDocument(id);
        _logger.LogDebug("Removed document {Id} from BM25", id);
    }

    /// <inheritdoc />
    public async Task RebuildIndexAsync(string userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebuilding BM25 index for user {UserId}", userId);

        var memories = await _memoryStore.GetAllAsync(userId, cancellationToken: cancellationToken);

        foreach (var memory in memories)
        {
            _bm25Index.AddDocument(memory.Id, memory.Content);
        }

        _logger.LogInformation("Indexed {Count} documents", memories.Count);
    }

    /// <summary>
    /// Applies Maximal Marginal Relevance (MMR) for result diversity.
    /// </summary>
    private static List<HybridSearchResult> ApplyMmrDiversity(
        List<HybridSearchResult> results,
        float lambda)
    {
        if (results.Count <= 1)
            return results;

        var selected = new List<HybridSearchResult> { results[0] };
        var remaining = results.Skip(1).ToList();

        while (remaining.Count > 0 && selected.Count < results.Count)
        {
            var bestScore = double.MinValue;
            HybridSearchResult? bestResult = null;

            foreach (var candidate in remaining)
            {
                // Calculate MMR score
                var relevance = candidate.Score;
                var maxSimilarity = selected
                    .Select(s => CalculateSimilarity(candidate.Memory, s.Memory))
                    .Max();

                var mmrScore = lambda * relevance - (1 - lambda) * maxSimilarity;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    bestResult = candidate;
                }
            }

            if (bestResult != null)
            {
                selected.Add(bestResult);
                remaining.Remove(bestResult);
            }
            else
            {
                break;
            }
        }

        return selected;
    }

    /// <summary>
    /// Calculates similarity between two memories using their embeddings.
    /// </summary>
    private static float CalculateSimilarity(MemoryUnit a, MemoryUnit b)
    {
        if (!a.Embedding.HasValue || !b.Embedding.HasValue)
            return 0f;

        var spanA = a.Embedding.Value.Span;
        var spanB = b.Embedding.Value.Span;

        if (spanA.Length != spanB.Length)
            return 0f;

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (var i = 0; i < spanA.Length; i++)
        {
            dotProduct += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }

        var magnitude = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return magnitude > 0 ? dotProduct / magnitude : 0f;
    }

    /// <summary>
    /// Generates query embedding with optional HyDE enhancement.
    /// </summary>
    private async Task<ReadOnlyMemory<float>> GenerateQueryEmbeddingAsync(
        string query,
        bool useHyde,
        int hydeDocCount,
        CancellationToken cancellationToken)
    {
        // Check if HyDE should be used based on query characteristics
        if (!useHyde || _hydeExpander is null)
        {
            return await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        }

        // Check minimum query length for HyDE
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < _options.HydeMinQueryWords)
        {
            _logger.LogDebug("Query too short for HyDE ({WordCount} words), using direct embedding", wordCount);
            return await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        }

        // Use HyDE: Generate hypothetical documents and average their embeddings
        if (hydeDocCount <= 1)
        {
            // Single hypothetical document
            return await _hydeExpander.GenerateHypotheticalEmbeddingAsync(query, cancellationToken);
        }

        // Ensemble: Average multiple hypothetical document embeddings
        var hydeEmbeddings = await _hydeExpander.GenerateMultipleHypotheticalEmbeddingsAsync(
            query, hydeDocCount, cancellationToken);

        if (hydeEmbeddings.Count == 0)
        {
            _logger.LogWarning("HyDE generated no embeddings, falling back to direct embedding");
            return await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        }

        if (hydeEmbeddings.Count == 1)
        {
            return hydeEmbeddings[0];
        }

        // Average the embeddings
        var dimensions = hydeEmbeddings[0].Length;
        var averaged = new float[dimensions];

        foreach (var embedding in hydeEmbeddings)
        {
            var span = embedding.Span;
            for (var i = 0; i < dimensions; i++)
            {
                averaged[i] += span[i];
            }
        }

        // Normalize by count and L2-normalize
        var count = hydeEmbeddings.Count;
        float norm = 0;
        for (var i = 0; i < dimensions; i++)
        {
            averaged[i] /= count;
            norm += averaged[i] * averaged[i];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                averaged[i] /= norm;
            }
        }

        _logger.LogDebug("HyDE ensemble: averaged {Count} hypothetical embeddings", count);
        return averaged;
    }

    private sealed class FusionScore
    {
        public required MemoryUnit Memory { get; init; }
        public float DenseScore { get; set; }
        public double DenseRrfScore { get; set; }
        public float SparseScore { get; set; }
        public double SparseRrfScore { get; set; }
        public double TotalRrfScore => DenseRrfScore + SparseRrfScore;
    }
}

/// <summary>
/// Hybrid search service interface.
/// </summary>
public interface IHybridSearchService
{
    /// <summary>
    /// Performs hybrid search combining dense and sparse retrieval.
    /// </summary>
    Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a document in the sparse index.
    /// </summary>
    void IndexDocument(Guid id, string content);

    /// <summary>
    /// Removes a document from the sparse index.
    /// </summary>
    void RemoveDocument(Guid id);

    /// <summary>
    /// Rebuilds the sparse index from stored memories.
    /// </summary>
    Task RebuildIndexAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for hybrid search.
/// </summary>
public sealed class HybridSearchOptions
{
    /// <summary>
    /// User ID to filter by.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session ID to filter by.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Minimum score threshold.
    /// </summary>
    public float? MinScore { get; set; }

    /// <summary>
    /// Weight for dense (vector) search (0.0 to 1.0).
    /// </summary>
    public float? DenseWeight { get; set; }

    /// <summary>
    /// Weight for sparse (BM25) search (0.0 to 1.0).
    /// </summary>
    public float? SparseWeight { get; set; }

    /// <summary>
    /// RRF k parameter for rank fusion.
    /// </summary>
    public int? RrfK { get; set; }

    /// <summary>
    /// Whether to apply MMR diversity filtering.
    /// </summary>
    public bool? UseMmr { get; set; }

    /// <summary>
    /// MMR lambda parameter (relevance vs diversity).
    /// </summary>
    public float? MmrLambda { get; set; }

    /// <summary>
    /// Memory types to include.
    /// </summary>
    public MemoryType[]? Types { get; set; }

    /// <summary>
    /// Filter by creation time (start).
    /// </summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>
    /// Filter by creation time (end).
    /// </summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Include soft-deleted memories.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Whether to use HyDE (Hypothetical Document Embeddings) for query.
    /// </summary>
    public bool? UseHyde { get; set; }

    /// <summary>
    /// Number of hypothetical documents to generate for HyDE ensemble.
    /// </summary>
    public int? HydeDocumentCount { get; set; }
}

/// <summary>
/// Result of a hybrid search operation.
/// </summary>
public sealed class HybridSearchResult
{
    /// <summary>
    /// The matched memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Combined score (RRF-fused).
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// Dense (vector) similarity score.
    /// </summary>
    public float DenseScore { get; init; }

    /// <summary>
    /// Sparse (BM25) score.
    /// </summary>
    public float SparseScore { get; init; }

    /// <summary>
    /// Which search method contributed to this result.
    /// </summary>
    public SearchType SearchType { get; init; }
}

/// <summary>
/// Type of search that found the result.
/// </summary>
public enum SearchType
{
    /// <summary>
    /// Result found by both dense and sparse search.
    /// </summary>
    Hybrid,

    /// <summary>
    /// Result found by dense (vector) search only.
    /// </summary>
    Dense,

    /// <summary>
    /// Result found by sparse (BM25) search only.
    /// </summary>
    Sparse
}
