using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.ContextOptimization;

/// <summary>
/// Service for optimizing context windows for LLM consumption.
/// Implements LongContextReorder, MMR diversity, and HyDE patterns.
/// </summary>
public interface IContextOptimizer
{
    /// <summary>
    /// Reorders memories using the LongContextReorder pattern.
    /// Places important content at beginning and end for better LLM attention.
    /// </summary>
    /// <param name="memories">Memories to reorder.</param>
    /// <returns>Reordered memories.</returns>
    IReadOnlyList<MemoryUnit> LongContextReorder(IEnumerable<MemoryUnit> memories);

    /// <summary>
    /// Applies Maximal Marginal Relevance (MMR) for diversity filtering.
    /// </summary>
    /// <param name="memories">Memories to filter.</param>
    /// <param name="queryEmbedding">Query embedding for relevance.</param>
    /// <param name="k">Number of results to return.</param>
    /// <param name="lambda">Balance between relevance and diversity (0-1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Diverse set of relevant memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> ApplyMMRAsync(
        IEnumerable<MemoryUnit> memories,
        ReadOnlyMemory<float> queryEmbedding,
        int k = 10,
        float lambda = 0.7f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates hypothetical document embedding (HyDE) for complex queries.
    /// </summary>
    /// <param name="query">The user query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enhanced query embedding.</returns>
    Task<HyDEResult> GenerateHyDEAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves parent-child chunks for context expansion.
    /// </summary>
    /// <param name="childMemory">The matched child chunk.</param>
    /// <param name="expandBefore">Number of chunks to include before.</param>
    /// <param name="expandAfter">Number of chunks to include after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expanded context with parent/sibling chunks.</returns>
    Task<ExpandedChunkResult> ExpandChunkContextAsync(
        MemoryUnit childMemory,
        int expandBefore = 1,
        int expandAfter = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes a set of memories for context window.
    /// Combines reordering, diversity, and compression as needed.
    /// </summary>
    /// <param name="memories">Memories to optimize.</param>
    /// <param name="options">Optimization options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Optimized memory set.</returns>
    Task<ContextOptimizationResult> OptimizeContextAsync(
        IEnumerable<MemoryUnit> memories,
        ContextOptimizationOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for context optimization.
/// </summary>
public sealed class ContextOptimizationOptions
{
    /// <summary>
    /// Maximum tokens allowed in context.
    /// </summary>
    public int MaxTokens { get; set; } = 128000;

    /// <summary>
    /// Target token count (may compress to achieve).
    /// </summary>
    public int TargetTokens { get; set; } = 100000;

    /// <summary>
    /// Whether to apply LongContextReorder.
    /// </summary>
    public bool EnableReordering { get; set; } = true;

    /// <summary>
    /// Whether to apply MMR diversity filtering.
    /// </summary>
    public bool EnableMMR { get; set; } = true;

    /// <summary>
    /// MMR lambda parameter (balance between relevance and diversity).
    /// </summary>
    public float MMRLambda { get; set; } = 0.7f;

    /// <summary>
    /// Whether to use HyDE for query enhancement.
    /// </summary>
    public bool EnableHyDE { get; set; } = false;

    /// <summary>
    /// Whether to expand chunk context.
    /// </summary>
    public bool EnableChunkExpansion { get; set; } = false;

    /// <summary>
    /// Query for relevance-based optimization.
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Query embedding if already computed.
    /// </summary>
    public ReadOnlyMemory<float>? QueryEmbedding { get; set; }
}

/// <summary>
/// Result of HyDE query enhancement.
/// </summary>
public sealed class HyDEResult
{
    /// <summary>
    /// The original query.
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// Generated hypothetical document.
    /// </summary>
    public string HypotheticalDocument { get; set; } = string.Empty;

    /// <summary>
    /// Enhanced embedding for retrieval.
    /// </summary>
    public ReadOnlyMemory<float> EnhancedEmbedding { get; set; }

    /// <summary>
    /// Original query embedding for comparison.
    /// </summary>
    public ReadOnlyMemory<float> OriginalEmbedding { get; set; }
}

/// <summary>
/// Result of chunk context expansion.
/// </summary>
public sealed class ExpandedChunkResult
{
    /// <summary>
    /// The original matched chunk.
    /// </summary>
    public MemoryUnit OriginalChunk { get; set; } = null!;

    /// <summary>
    /// Preceding context chunks.
    /// </summary>
    public List<MemoryUnit> PrecedingChunks { get; set; } = [];

    /// <summary>
    /// Following context chunks.
    /// </summary>
    public List<MemoryUnit> FollowingChunks { get; set; } = [];

    /// <summary>
    /// Combined expanded content.
    /// </summary>
    public string ExpandedContent => string.Join("\n\n",
        PrecedingChunks.Select(c => c.Content)
            .Append(OriginalChunk.Content)
            .Concat(FollowingChunks.Select(c => c.Content)));
}

/// <summary>
/// Result of context optimization.
/// </summary>
public sealed class ContextOptimizationResult
{
    /// <summary>
    /// Optimized memories.
    /// </summary>
    public List<MemoryUnit> OptimizedMemories { get; set; } = [];

    /// <summary>
    /// Original token count.
    /// </summary>
    public int OriginalTokenCount { get; set; }

    /// <summary>
    /// Final token count.
    /// </summary>
    public int FinalTokenCount { get; set; }

    /// <summary>
    /// Optimizations applied.
    /// </summary>
    public List<string> OptimizationsApplied { get; set; } = [];

    /// <summary>
    /// Memories removed during optimization.
    /// </summary>
    public int MemoriesRemoved { get; set; }

    /// <summary>
    /// Whether target token count was achieved.
    /// </summary>
    public bool TargetAchieved { get; set; }
}
